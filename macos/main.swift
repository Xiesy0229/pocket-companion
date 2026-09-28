import AppKit

struct Preferences: Codable {
    var x: Double? = nil
    var y: Double? = nil
    var scale: Double = 0.5
    var snacks: Int = 0
    var phrases = ["今日はどんな一日だった？", "ちょっと休憩しよ。", "何か音楽でも聴く？", "ここにいるよ。"]
    mutating func normalize() {
        scale = scale.isFinite ? min(1.2, max(0.4, scale)) : 0.5
        phrases = Array(phrases.map { String($0.trimmingCharacters(in: .whitespacesAndNewlines).prefix(36)) }.filter { !$0.isEmpty }.prefix(100))
        if phrases.isEmpty { phrases = ["ここにいるよ。"] }
        snacks = max(0, snacks)
    }
    static func load(_ url: URL) -> Preferences {
        guard let data = try? Data(contentsOf: url), var value = try? JSONDecoder().decode(Preferences.self, from: data) else { return Preferences() }
        value.normalize()
        return value
    }
    func save(_ url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(self).write(to: url, options: .atomic)
    }
}

final class PetModel {
    var preferences: Preferences
    var bubble = "こんにちは。"
    var bubbleUntil = Date.timeIntervalSinceReferenceDate + 6
    var bounceUntil: Double = 0
    var eatUntil: Double = 0
    var phraseIndex = 0
    let imageRect: NSRect
    var companion: NSRect { NSRect(x: 10 + 340 * 0.64, y: 84 + imageRect.height * 0.68, width: 340 * 0.32, height: imageRect.height * 0.29) }
    var mouth: NSPoint { NSPoint(x: 10 + 340 * 0.774, y: 84 + imageRect.height * 0.811) }
    var cookie: NSRect { NSRect(x: 128, y: imageRect.maxY + 7, width: 104, height: 36) }
    var height: CGFloat { cookie.maxY + 8 }
    init(_ preferences: Preferences, aspect: CGFloat) {
        self.preferences = preferences
        imageRect = NSRect(x: 10, y: 84, width: 340, height: 340 * aspect)
    }
    func say(_ now: Double) {
        bubble = preferences.phrases[phraseIndex % preferences.phrases.count]
        phraseIndex = (phraseIndex + 1) % preferences.phrases.count
        bubbleUntil = now + 4
        bounceUntil = now + 0.65
    }
    @discardableResult func feed(_ point: NSPoint, _ now: Double) -> Bool {
        guard companion.contains(point) else { return false }
        preferences.snacks += 1
        eatUntil = now + 1.8
        bounceUntil = now + 0.65
        bubble = "クッキー、ありがとう！"
        bubbleUntil = now + 3
        return true
    }
}

final class PetWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class PetView: NSView {
    let model: PetModel
    let image: NSImage
    let pixels: NSBitmapImageRep
    var onSave: (() -> Void)?
    var onMenu: ((NSEvent) -> Void)?
    var pressed = false
    var feeding = false
    var moved = false
    var food = NSPoint.zero
    var downMouse = NSPoint.zero
    var downOrigin = NSPoint.zero
    var nextDraw: Double = 0
    var clock: Timer?
    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    init(model: PetModel, image: NSImage, pixels: NSBitmapImageRep) {
        self.model = model; self.image = image; self.pixels = pixels
        super.init(frame: NSRect(x: 0, y: 0, width: 360 * model.preferences.scale, height: model.height * model.preferences.scale))
    }
    required init?(coder: NSCoder) { fatalError("Not used") }
    func startClock() {
        clock = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            guard let self = self, let window = self.window, window.isVisible else { return }
            let now = Date.timeIntervalSinceReferenceDate
            if !self.pressed {
                let p = window.convertPoint(fromScreen: NSEvent.mouseLocation)
                let local = self.convert(p, from: nil)
                window.ignoresMouseEvents = !self.interactive(NSPoint(x: local.x / self.model.preferences.scale, y: local.y / self.model.preferences.scale), now)
            }
            if self.pressed || now < self.model.bounceUntil || now < self.model.eatUntil || now >= self.nextDraw {
                self.needsDisplay = true
                self.nextDraw = now + 0.15
            }
        }
    }
    func interactive(_ p: NSPoint, _ now: Double) -> Bool {
        if model.cookie.contains(p) { return true }
        if now < model.bubbleUntil && NSRect(x: 31, y: 9, width: 298, height: 68).contains(p) { return true }
        guard model.imageRect.contains(p) else { return false }
        let x = min(pixels.pixelsWide - 1, max(0, Int((p.x - 10) / 340 * CGFloat(pixels.pixelsWide))))
        let y = min(pixels.pixelsHigh - 1, max(0, Int((p.y - 84) / model.imageRect.height * CGFloat(pixels.pixelsHigh))))
        return (pixels.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 0.05
    }
    func point(_ event: NSEvent) -> NSPoint {
        let p = convert(event.locationInWindow, from: nil)
        return NSPoint(x: p.x / model.preferences.scale, y: p.y / model.preferences.scale)
    }
    override func mouseDown(with event: NSEvent) {
        pressed = true; moved = false; food = point(event)
        feeding = model.cookie.contains(food)
        downMouse = NSEvent.mouseLocation; downOrigin = window?.frame.origin ?? .zero
    }
    override func mouseDragged(with event: NSEvent) {
        if feeding { food = point(event); needsDisplay = true; return }
        let mouse = NSEvent.mouseLocation
        let dx = mouse.x - downMouse.x, dy = mouse.y - downMouse.y
        if abs(dx) + abs(dy) > 5 { moved = true }
        if moved { window?.setFrameOrigin(NSPoint(x: downOrigin.x + dx, y: downOrigin.y + dy)) }
    }
    override func mouseUp(with event: NSEvent) {
        guard pressed else { return }
        if feeding { if model.feed(point(event), Date.timeIntervalSinceReferenceDate) { onSave?() } }
        else if moved { clamp(); onSave?() }
        else { model.say(Date.timeIntervalSinceReferenceDate) }
        pressed = false; feeding = false; needsDisplay = true
    }
    override func rightMouseDown(with event: NSEvent) { onMenu?(event) }
    func clamp() {
        guard let window = window, let screen = window.screen ?? NSScreen.main else { return }
        let a = screen.visibleFrame
        window.setFrameOrigin(NSPoint(x: max(a.minX, min(window.frame.minX, a.maxX - window.frame.width)), y: max(a.minY, min(window.frame.minY, a.maxY - window.frame.height))))
    }
    func ink(_ color: NSColor, _ rect: NSRect) {
        let points = [NSPoint(x: rect.minX + 8, y: rect.minY + 1), NSPoint(x: rect.midX, y: rect.minY), NSPoint(x: rect.maxX - 8, y: rect.minY + 2), NSPoint(x: rect.maxX, y: rect.minY + 9), NSPoint(x: rect.maxX - 1, y: rect.maxY - 7), NSPoint(x: rect.maxX - 9, y: rect.maxY), NSPoint(x: rect.midX, y: rect.maxY - 1), NSPoint(x: rect.minX + 7, y: rect.maxY), NSPoint(x: rect.minX, y: rect.maxY - 8), NSPoint(x: rect.minX + 1, y: rect.minY + 8)]
        let path = NSBezierPath(); path.move(to: points[0]); points.dropFirst().forEach { path.line(to: $0) }; path.close()
        NSColor(calibratedRed: 1, green: 0.99, blue: 0.91, alpha: 1).setFill(); path.fill()
        color.withAlphaComponent(0.3).setStroke(); path.lineWidth = 4; path.stroke()
        color.withAlphaComponent(0.85).setStroke(); path.lineWidth = 1.5
        path.setLineDash([4, 0.8, 1, 0.6, 3, 0.7], count: 6, phase: 0); path.stroke()
    }
    func text(_ value: String, in rect: NSRect, size: CGFloat) {
        let style = NSMutableParagraphStyle(); style.alignment = .center; style.lineBreakMode = .byWordWrapping
        (value as NSString).draw(in: rect, withAttributes: [.font: NSFont.systemFont(ofSize: size, weight: .medium), .foregroundColor: NSColor(calibratedRed: 0.13, green: 0.22, blue: 0.24, alpha: 1), .paragraphStyle: style])
    }
    func cookie(_ p: NSPoint) {
        let path = NSBezierPath(ovalIn: NSRect(x: p.x - 13, y: p.y - 13, width: 26, height: 26))
        NSColor(calibratedRed: 0.9, green: 0.68, blue: 0.36, alpha: 1).setFill(); path.fill()
        NSColor.brown.setStroke(); path.lineWidth = 2; path.stroke()
        NSColor.brown.setFill()
        for d in [NSPoint(x: -6, y: -6), NSPoint(x: 4, y: -3), NSPoint(x: -2, y: 5)] { NSRect(x: p.x + d.x, y: p.y + d.y, width: 4, height: 4).fill() }
    }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.setFill(); bounds.fill(using: .copy)
        NSGraphicsContext.saveGraphicsState()
        let transform = NSAffineTransform(); transform.scale(by: model.preferences.scale); transform.concat()
        let now = Date.timeIntervalSinceReferenceDate
        let bounce = now < model.bounceUntil ? -abs(sin((model.bounceUntil - now) * .pi * 3)) * 8 : 0
        var rect = model.imageRect; rect.origin.y += bounce
        image.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.high])
        if now < model.eatUntil {
            let m = NSPoint(x: model.mouth.x, y: model.mouth.y + bounce)
            let opening = 2 + abs(sin(now * 15)) * 5
            NSColor(calibratedRed: 0.13, green: 0.22, blue: 0.24, alpha: 1).setFill()
            NSBezierPath(ovalIn: NSRect(x: m.x - 9, y: m.y - opening, width: 18, height: 2 * opening)).fill()
            for i in 0..<5 { let age = (now * 1.8 + Double(i) * 0.19).truncatingRemainder(dividingBy: 1)
                NSColor(calibratedRed: 0.87, green: 0.65, blue: 0.36, alpha: 1 - age).setFill()
                NSRect(x: m.x + Double(i - 2) * 9 * age, y: m.y + 20 * age, width: 3, height: 3).fill()
            }
        }
        let outline = NSColor(calibratedRed: 0.49, green: 0.59, blue: 0.24, alpha: 1)
        ink(outline, model.cookie)
        if !feeding { cookie(NSPoint(x: model.cookie.minX + 20, y: model.cookie.minY + 18)) }
        text("おやつ", in: NSRect(x: model.cookie.minX + 36, y: model.cookie.minY + 7, width: 65, height: 26), size: max(12, 7 / model.preferences.scale))
        if feeding { cookie(food) }
        if now < model.bubbleUntil {
            ink(outline, NSRect(x: 31, y: 9, width: 298, height: 56))
            let tail = NSBezierPath(); tail.move(to: NSPoint(x: 166, y: 65)); tail.line(to: NSPoint(x: 170, y: 76)); tail.line(to: NSPoint(x: 183, y: 66)); outline.setStroke(); tail.lineWidth = 2; tail.stroke()
            text(model.bubble, in: NSRect(x: 42, y: 22, width: 276, height: 42), size: max(13, 10 / model.preferences.scale))
        }
        NSGraphicsContext.restoreGraphicsState()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    var petWindow: PetWindow!
    var pet: PetView!
    var status: NSStatusItem!
    let settingsURL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("PocketCompanion/settings.json")
    func applicationDidFinishLaunching(_ notification: Notification) {
        guard let asset = Bundle.main.url(forResource: "duo", withExtension: "png"), let data = try? Data(contentsOf: asset), let image = NSImage(data: data), let pixels = NSBitmapImageRep(data: data) else { fatalError("Missing duo.png in app Resources") }
        let model = PetModel(Preferences.load(settingsURL), aspect: CGFloat(pixels.pixelsHigh) / CGFloat(pixels.pixelsWide))
        pet = PetView(model: model, image: image, pixels: pixels)
        petWindow = PetWindow(contentRect: pet.frame, styleMask: .borderless, backing: .buffered, defer: false)
        petWindow.isOpaque = false; petWindow.backgroundColor = .clear; petWindow.hasShadow = false
        petWindow.level = .floating; petWindow.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        petWindow.isReleasedWhenClosed = false; petWindow.contentView = pet
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 800)
        petWindow.setFrameOrigin(NSPoint(x: model.preferences.x ?? screen.maxX - pet.frame.width - 20, y: model.preferences.y ?? screen.minY + 20))
        pet.clamp()
        pet.onSave = { [weak self] in self?.save() }
        pet.onMenu = { [weak self] event in guard let self = self else { return }; NSMenu.popUpContextMenu(self.makeMenu(), with: event, for: self.pet) }
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        status.button?.title = "🍪"; status.menu = makeMenu()
        petWindow.orderFrontRegardless(); pet.startClock()
        if CommandLine.arguments.contains("--smoke-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
                guard let bitmap = self.pet.bitmapImageRepForCachingDisplay(in: self.pet.bounds) else { exit(1) }
                self.pet.cacheDisplay(in: self.pet.bounds, to: bitmap)
                guard let png = bitmap.representation(using: .png, properties: [:]) else { exit(1) }
                try! png.write(to: URL(fileURLWithPath: "macos-preview.png"))
                print("PASS: AppKit transparent window, asset loading and view rendering")
                NSApp.terminate(nil)
            }
        }
    }
    func makeMenu() -> NSMenu {
        let menu = NSMenu()
        for (title, action) in [("显示桌宠", #selector(showPet)), ("说一句 / ひとこと", #selector(say)), ("修改短句…", #selector(editPhrases))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: ""); item.target = self; menu.addItem(item)
        }
        let sizes = NSMenu(); let parent = NSMenuItem(title: "桌宠大小", action: nil, keyEquivalent: ""); parent.submenu = sizes
        for size in [40, 50, 70, 100, 120] { let item = NSMenuItem(title: "\(size)%", action: #selector(resize(_:)), keyEquivalent: ""); item.tag = size; item.target = self; sizes.addItem(item) }
        menu.addItem(parent)
        for (title, action) in [("回到屏幕右下角", #selector(reset)), ("暂时隐藏", #selector(hidePet)), ("退出", #selector(quit))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: ""); item.target = self; menu.addItem(item)
        }
        return menu
    }
    func save() {
        guard pet != nil else { return }
        pet.model.preferences.x = petWindow.frame.origin.x; pet.model.preferences.y = petWindow.frame.origin.y
        do { try pet.model.preferences.save(settingsURL) }
        catch { pet.model.bubble = "保存できませんでした。"; pet.model.bubbleUntil = Date.timeIntervalSinceReferenceDate + 4 }
    }
    @objc func showPet() { pet.clamp(); petWindow.orderFrontRegardless() }
    @objc func say() { showPet(); pet.model.say(Date.timeIntervalSinceReferenceDate) }
    @objc func hidePet() { petWindow.orderOut(nil) }
    @objc func quit() { NSApp.terminate(nil) }
    @objc func reset() {
        if let screen = NSScreen.main { petWindow.setFrameOrigin(NSPoint(x: screen.visibleFrame.maxX - petWindow.frame.width - 20, y: screen.visibleFrame.minY + 20)) }
        showPet(); save()
    }
    @objc func resize(_ sender: NSMenuItem) {
        pet.model.preferences.scale = Double(sender.tag) / 100
        petWindow.setContentSize(NSSize(width: 360 * pet.model.preferences.scale, height: pet.model.height * pet.model.preferences.scale))
        pet.clamp(); save(); pet.needsDisplay = true
    }
    @objc func editPhrases() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert(); alert.messageText = "我的短句 · 一行一句"; alert.informativeText = "最多 100 句，每句最多 36 个字。点击人物时轮流显示。"
        alert.addButton(withTitle: "保存"); alert.addButton(withTitle: "取消")
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 360, height: 180)); scroll.hasVerticalScroller = true
        let editor = NSTextView(frame: scroll.bounds); editor.isRichText = false; editor.font = .systemFont(ofSize: 14)
        editor.string = pet.model.preferences.phrases.joined(separator: "\n"); scroll.documentView = editor; alert.accessoryView = scroll
        alert.window.initialFirstResponder = editor
        if alert.runModal() == .alertFirstButtonReturn {
            pet.model.preferences.phrases = editor.string.components(separatedBy: .newlines)
            pet.model.preferences.normalize(); pet.model.phraseIndex = 0; save()
        }
    }
    func applicationWillTerminate(_ notification: Notification) { if !CommandLine.arguments.contains("--smoke-test") { save() } }
}

func selfTest() throws {
    let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    defer { try? FileManager.default.removeItem(at: dir) }
    let file = dir.appendingPathComponent("settings.json")
    var p = Preferences(); p.x = -450; p.y = 120; p.phrases = ["日本語", "二番目"]
    try p.save(file); let loaded = Preferences.load(file)
    precondition(loaded.x == -450 && loaded.phrases == p.phrases)
    let m = PetModel(loaded, aspect: 1.2)
    precondition(!m.feed(.zero, 1) && m.preferences.snacks == 0)
    precondition(m.feed(m.mouth, 2) && m.preferences.snacks == 1 && m.eatUntil > 2)
    m.say(4); precondition(m.bubble == "日本語"); m.say(5); m.say(6); precondition(m.bubble == "日本語")
    try Data("broken".utf8).write(to: file); precondition(Preferences.load(file).scale == 0.5)
    guard let asset = Bundle.main.url(forResource: "duo", withExtension: "png"), let data = try? Data(contentsOf: asset), let bitmap = NSBitmapImageRep(data: data) else { fatalError("Asset missing") }
    precondition(bitmap.hasAlpha && (bitmap.colorAt(x: 0, y: 0)?.alphaComponent ?? 1) == 0)
    print("PASS: settings persistence, invalid settings recovery, feeding hit/miss, Japanese phrase cycle, bundled transparent art")
}

if CommandLine.arguments.contains("--self-test") {
    do { try selfTest() } catch { fputs("Self-test failed: \(error)\n", stderr); exit(1) }
} else {
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    let delegate = AppDelegate(); app.delegate = delegate
    app.run()
}
