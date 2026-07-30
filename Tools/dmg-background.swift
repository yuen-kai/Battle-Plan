// Renders the Battle Plan disk-image backdrop.
//
//   swift Tools/dmg-background.swift out.png [scale]
//
// The canvas matches the Finder window's content size in points; pass scale 2
// for the Retina representation. Icon slots are left empty because Finder draws
// the app and the Applications alias on top at the positions the packaging
// script assigns.

import AppKit

let outPath = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "dmg-background.png"
let scale = CommandLine.arguments.count > 2 ? CGFloat(Double(CommandLine.arguments[2]) ?? 1) : 1

let W: CGFloat = 700
let H: CGFloat = 600

let fontDir = URL(fileURLWithPath: #filePath)
	.deletingLastPathComponent()
	.deletingLastPathComponent()
	.appendingPathComponent("Assets/Fonts")

for path in ["Rubik/Rubik-VariableFont_wght.ttf", "CascadiaCode-VariableFont_wght.ttf"] {
	CTFontManagerRegisterFontsForURL(fontDir.appendingPathComponent(path) as CFURL, .process, nil)
}

func variable(_ family: String, _ size: CGFloat, _ weight: CGFloat, fallback: NSFont) -> NSFont {
	let wght = 0x77676874
	let descriptor = CTFontDescriptorCreateWithAttributes([
		kCTFontFamilyNameAttribute: family,
		kCTFontVariationAttribute: [wght: weight],
	] as CFDictionary)
	let font = CTFontCreateWithFontDescriptor(descriptor, size, nil) as NSFont
	return font.familyName == family ? font : fallback
}

func rubik(_ size: CGFloat, _ weight: CGFloat) -> NSFont {
	variable("Rubik", size, weight, fallback: .systemFont(ofSize: size))
}

func mono(_ size: CGFloat, _ weight: CGFloat) -> NSFont {
	variable("Cascadia Code", size, weight, fallback: .monospacedSystemFont(ofSize: size, weight: .regular))
}

func rgb(_ r: Int, _ g: Int, _ b: Int, _ a: CGFloat = 1) -> NSColor {
	NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: a)
}

// Mirrors Assets/UI/Shared/TacticalToyboxTokens.uss.
let ground = rgb(22, 26, 27)
let surface = rgb(29, 34, 35)
let bodyText = rgb(214, 219, 221)
let copyText = rgb(179, 193, 196)
let mutedText = rgb(148, 160, 163)
let divider = rgb(69, 67, 70)
let primary = rgb(241, 143, 1)

// Explicit sRGB rather than device RGB, so the label plates' measured contrast
// is a property of the file and not of the build machine's display profile.
guard let space = CGColorSpace(name: CGColorSpace.sRGB),
      let ctx = CGContext(
      	data: nil,
      	width: Int(W * scale), height: Int(H * scale),
      	bitsPerComponent: 8, bytesPerRow: 0, space: space,
      	bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
      ) else { exit(1) }
ctx.scaleBy(x: scale, y: scale)

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)

/// Converts a top-left y coordinate to AppKit's bottom-left space.
func flip(_ topY: CGFloat) -> CGFloat { H - topY }

@discardableResult
func draw(_ string: String, x: CGFloat, topY: CGFloat, font: NSFont, color: NSColor, tracking: CGFloat = 0) -> NSSize {
	var attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
	if tracking != 0 { attrs[.kern] = tracking }
	let text = NSAttributedString(string: string, attributes: attrs)
	let size = text.size()
	text.draw(at: NSPoint(x: x, y: flip(topY) - size.height))
	return size
}

func drawCentered(_ string: String, centerX: CGFloat, topY: CGFloat, font: NSFont, color: NSColor, tracking: CGFloat = 0) {
	var attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
	if tracking != 0 { attrs[.kern] = tracking }
	let text = NSAttributedString(string: string, attributes: attrs)
	let size = text.size()
	text.draw(at: NSPoint(x: centerX - size.width / 2, y: flip(topY) - size.height))
}

// Ground gradient.
NSGradient(colors: [surface, ground])?.draw(in: NSRect(x: 0, y: 0, width: W, height: H), angle: -90)

// Faint tactical grid, echoing the game board.
let grid = NSBezierPath()
grid.lineWidth = 1
for x in stride(from: 0, through: W, by: 40) {
	grid.move(to: NSPoint(x: x, y: 0)); grid.line(to: NSPoint(x: x, y: H))
}
for y in stride(from: 0, through: H, by: 40) {
	grid.move(to: NSPoint(x: 0, y: y)); grid.line(to: NSPoint(x: W, y: y))
}
rgb(255, 255, 255, 0.022).setStroke()
grid.stroke()

// Warm pool under the app icon so the eye starts on the thing to drag.
ctx.saveGState()
let glow = NSRect(x: 175 - 120, y: flip(205) - 120, width: 240, height: 240)
NSGradient(colors: [primary.withAlphaComponent(0.07), primary.withAlphaComponent(0)])?
	.draw(in: NSBezierPath(ovalIn: glow), relativeCenterPosition: .zero)
ctx.restoreGState()

// Finder paints icon labels in the system label colour - black in Light Mode,
// white in Dark - and a fixed backdrop cannot satisfy both. Each label instead
// sits on a plate held near relative luminance 0.18, the point where contrast
// against black and against white are equal, so either renders at about 4.6:1.
// The plates must line up with the icon slots in package-mac-build.sh.
let plateFill = rgb(110, 118, 122)
for slot in [(x: CGFloat(175), y: CGFloat(205)), (x: 525, y: 205), (x: 175, y: 425)] {
	let plate = NSBezierPath(
		roundedRect: NSRect(x: slot.x - 95, y: flip(slot.y + 92), width: 190, height: 30),
		xRadius: 6, yRadius: 6
	)
	plateFill.setFill()
	plate.fill()
}

// Header.
let wordmark = draw("BATTLE PLAN", x: 44, topY: 40, font: rubik(21, 600), color: bodyText, tracking: 3.4)
draw("INSTALLER", x: 44 + wordmark.width + 14, topY: 45, font: mono(10, 400), color: mutedText, tracking: 2.2)

let rule = NSBezierPath()
rule.move(to: NSPoint(x: 44, y: flip(82)))
rule.line(to: NSPoint(x: W - 44, y: flip(82)))
rule.lineWidth = 1
divider.setStroke()
rule.stroke()

func stepLabel(_ number: String, _ title: String, _ detail: String, topY: CGFloat) {
	let radius: CGFloat = 9
	let circle = NSBezierPath(ovalIn: NSRect(x: 44, y: flip(topY) - radius * 2, width: radius * 2, height: radius * 2))
	primary.setFill()
	circle.fill()
	drawCentered(number, centerX: 44 + radius, topY: topY + 3.5, font: rubik(11, 700), color: ground)

	let titleSize = draw(title, x: 44 + radius * 2 + 12, topY: topY + 2, font: rubik(12, 600), color: primary, tracking: 1.9)
	draw(detail, x: 44 + radius * 2 + 12 + titleSize.width + 14, topY: topY + 3, font: rubik(11.5, 400), color: mutedText)
}

stepLabel("1", "INSTALL", "Drag Battle Plan into your Applications folder.", topY: 108)

// Drag arrow. Dashes read as motion; the head fixes the direction.
ctx.saveGState()
let arrowY = flip(205)
let shaft = NSBezierPath()
shaft.move(to: NSPoint(x: 268, y: arrowY))
shaft.line(to: NSPoint(x: 418, y: arrowY))
shaft.lineWidth = 2.5
shaft.lineCapStyle = .round
shaft.setLineDash([9, 8], count: 2, phase: 0)
primary.withAlphaComponent(0.85).setStroke()
shaft.stroke()

let head = NSBezierPath()
head.move(to: NSPoint(x: 438, y: arrowY))
head.line(to: NSPoint(x: 416, y: arrowY + 11))
head.line(to: NSPoint(x: 416, y: arrowY - 11))
head.close()
primary.setFill()
head.fill()
ctx.restoreGState()

// Quiet separator rather than a boxed panel, so both steps read as the same
// kind of row: a label, an icon, and its explanation.
let split = NSBezierPath()
split.move(to: NSPoint(x: 44, y: flip(310)))
split.line(to: NSPoint(x: W - 44, y: flip(310)))
split.lineWidth = 1
divider.withAlphaComponent(0.6).setStroke()
split.stroke()

stepLabel("2", "FIRST LAUNCH", "macOS blocks apps it cannot verify.", topY: 336)

// The shortcut icon Finder draws at (175, 425) opens Privacy & Security, so the
// numbered lines sit to its right and refer back to it.
let unblockSteps = [
	"Launch Battle Plan, then click Done on the warning.",
	"Open Settings at left to reach Privacy & Security.",
	"Scroll to Security and click Open Anyway.",
]
for (index, line) in unblockSteps.enumerated() {
	let lineTop = 380 + CGFloat(index) * 30
	draw("\(index + 1)", x: 290, topY: lineTop + 0.5, font: rubik(11.5, 700), color: primary)
	draw(line, x: 308, topY: lineTop, font: rubik(12.5, 400), color: copyText)
}

drawCentered(
	"Only needed the first time \u{2014} this build is signed, but not notarised by Apple.",
	centerX: W / 2, topY: 556, font: rubik(10.5, 400), color: mutedText
)

NSGraphicsContext.restoreGraphicsState()

guard let image = ctx.makeImage() else { exit(1) }
let rep = NSBitmapImageRep(cgImage: image)
rep.size = NSSize(width: W, height: H)
guard let png = rep.representation(using: .png, properties: [:]) else { exit(1) }
try png.write(to: URL(fileURLWithPath: outPath))
