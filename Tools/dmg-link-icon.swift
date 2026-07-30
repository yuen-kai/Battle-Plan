// Gives the disk image's settings shortcut a themed icon.
//
//   swift Tools/dmg-link-icon.swift "path/to/Open Settings.inetloc"
//
// Without this the file shows Finder's generic white INETLOC document, which
// reads as debris next to the app rather than as the action to take.

import AppKit

guard CommandLine.arguments.count > 1 else { exit(2) }
let target = CommandLine.arguments[1]

let side: CGFloat = 512

func rgb(_ r: Int, _ g: Int, _ b: Int, _ a: CGFloat = 1) -> NSColor {
	NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: a)
}

// Primary carries "the next decision" in TacticalToyboxTokens.uss, which is
// exactly what this shortcut is.
let surface = rgb(29, 34, 35)
let primary = rgb(241, 143, 1)

func tinted(_ image: NSImage, _ color: NSColor) -> NSImage {
	let output = NSImage(size: image.size)
	output.lockFocus()
	let bounds = NSRect(origin: .zero, size: image.size)
	image.draw(in: bounds)
	color.set()
	bounds.fill(using: .sourceAtop)
	output.unlockFocus()
	return output
}

let icon = NSImage(size: NSSize(width: side, height: side))
icon.lockFocus()

let plate = NSBezierPath(
	roundedRect: NSRect(x: 36, y: 36, width: side - 72, height: side - 72),
	xRadius: 96, yRadius: 96
)
surface.setFill()
plate.fill()
plate.lineWidth = 14
primary.withAlphaComponent(0.9).setStroke()
plate.stroke()

let config = NSImage.SymbolConfiguration(pointSize: 250, weight: .semibold)
if let symbol = NSImage(systemSymbolName: "gearshape.fill", accessibilityDescription: nil)?
	.withSymbolConfiguration(config) {
	let glyph = tinted(symbol, primary)
	glyph.draw(in: NSRect(
		x: (side - glyph.size.width) / 2,
		y: (side - glyph.size.height) / 2,
		width: glyph.size.width,
		height: glyph.size.height
	))
}

icon.unlockFocus()

exit(NSWorkspace.shared.setIcon(icon, forFile: target, options: []) ? 0 : 1)
