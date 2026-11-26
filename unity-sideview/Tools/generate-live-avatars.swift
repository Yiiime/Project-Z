// Offline vector artwork: 128 distinct island-object avatars. No runtime network or generation.
// swift Tools/generate-live-avatars.swift Assets/Seabreeze/Resources/FirstDay/LiveAvatars.png
import AppKit
let output = CommandLine.arguments[1]
let bitmap = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:2048,pixelsHigh:1024,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep:bitmap)
let ink=NSColor(calibratedRed:0.19,green:0.24,blue:0.30,alpha:1)
func oval(_ x:CGFloat,_ y:CGFloat,_ w:CGFloat,_ h:CGFloat,_ color:NSColor){color.setFill();NSBezierPath(ovalIn:NSRect(x:x,y:y,width:w,height:h)).fill()}
func rect(_ x:CGFloat,_ y:CGFloat,_ w:CGFloat,_ h:CGFloat,_ color:NSColor,_ r:CGFloat=8){color.setFill();let p=NSBezierPath(roundedRect:NSRect(x:x,y:y,width:w,height:h),xRadius:r,yRadius:r);p.fill();ink.setStroke();p.lineWidth=4;p.stroke()}
func line(_ pts:[(CGFloat,CGFloat)],_ color:NSColor=ink,_ width:CGFloat=5,closed:Bool=false,fill:NSColor?=nil){let p=NSBezierPath();p.move(to:NSPoint(x:pts[0].0,y:pts[0].1));for a in pts.dropFirst(){p.line(to:NSPoint(x:a.0,y:a.1))};if closed{p.close()};if let f=fill{f.setFill();p.fill()};color.setStroke();p.lineWidth=width;p.lineCapStyle = .round;p.lineJoinStyle = .round;p.stroke()}
func star(_ x:CGFloat,_ y:CGFloat,_ radius:CGFloat,_ c:NSColor){let pts=(0..<10).map{ i -> (CGFloat,CGFloat) in let a=CGFloat(i)*CGFloat.pi/5+CGFloat.pi/2;let r=i%2==0 ? radius:radius*0.44;return(x+cos(a)*r,y+sin(a)*r)};line(pts,ink,4,closed:true,fill:c)}
for index in 0..<128{
 NSGraphicsContext.saveGraphicsState();let t=NSAffineTransform();let tileX=CGFloat(index % 16)*128.0;let tileY=CGFloat(index / 16)*128.0;t.translateX(by:tileX,yBy:tileY);t.concat()
 let variant=index/16;let hue=CGFloat((index%16*23+variant*37)%360)/360
 let bg=NSColor(calibratedHue:hue,saturation:0.16+CGFloat(variant)*0.012,brightness:0.98,alpha:1)
 let accent=NSColor(calibratedHue:hue,saturation:0.38,brightness:0.77,alpha:1)
 let light=NSColor(calibratedRed:1,green:0.94,blue:0.76,alpha:1)
 oval(2,2,124,124,bg);oval(20,15,88,16,NSColor(calibratedWhite:0.2,alpha:0.06))
 switch index%16 {
 case 0: oval(28,28,70,70,light);oval(49,46,58,58,bg);star(89,39,10,accent);star(37,93,7,accent)
 case 1: line([(26,30),(26,92),(51,82),(77,94),(103,83),(103,23),(77,34),(51,22)],ink,4,closed:true,fill:light);line([(51,82),(51,22)]);line([(77,94),(77,34)]);line([(35,45),(44,60),(63,50),(84,66)],accent,4);oval(79,62,12,12,accent)
 case 2: rect(33,32,56,49,light,10);let p=NSBezierPath(ovalIn:NSRect(x:79,y:44,width:25,height:28));ink.setStroke();p.lineWidth=5;p.stroke();line([(24,27),(101,27)],accent,6);line([(46,91),(43,101)],accent,4);line([(65,91),(69,105)],accent,4)
 case 3: for k in 0..<8{let a=CGFloat(k)*CGFloat.pi/4;line([(64+cos(a)*23,65+sin(a)*23),(64+cos(a)*39,65+sin(a)*39)],accent,14)};oval(35,36,58,58,accent);oval(48,49,32,32,light);line([(25,28),(44,47)],ink,7)
 case 4: line([(64,103),(97,88),(90,46),(64,24),(38,46),(31,88)],ink,4,closed:true,fill:accent);star(64,66,24,light)
 case 5: line([(64,29),(64,68)],ink,5);line([(63,43),(40,51)],accent,9);line([(65,35),(87,48)],accent,9);for k in 0..<5{let a=CGFloat(k)*CGFloat.pi*2/5;oval(52+cos(a)*21,62+sin(a)*21,25,25,accent)};oval(54,64,21,21,light)
 case 6: let p=NSBezierPath(ovalIn:NSRect(x:54,y:86,width:20,height:20));ink.setStroke();p.lineWidth=5;p.stroke();line([(64,88),(64,27)]);line([(41,73),(87,73)]);line([(28,56),(35,38),(64,24),(93,38),(100,56)]);line([(23,49),(28,59),(39,55)]);line([(89,55),(100,59),(105,49)])
 case 7: rect(24,35,81,56,accent);rect(38,88,29,12,light,3);oval(43,43,43,43,ink);oval(49,49,31,31,light);oval(57,57,15,15,accent);oval(91,77,7,7,light)
 case 8: oval(27,27,76,76,accent);oval(34,34,62,62,light);line([(50,47),(62,72),(83,88),(68,60)],ink,3,closed:true,fill:accent);line([(64,103),(64,94)]);line([(25,65),(34,65)])
 case 9: line([(24,31),(24,94),(64,82),(104,94),(104,31),(64,20)],ink,4,closed:true,fill:light);line([(64,82),(64,20)]);line([(34,76),(52,70)],accent,4);line([(76,69),(93,75)],accent,4);line([(34,62),(52,56)],accent,4)
 case 10: let p=NSBezierPath();p.move(to:NSPoint(x:24,y:61));p.curve(to:NSPoint(x:104,y:61),controlPoint1:NSPoint(x:31,y:112),controlPoint2:NSPoint(x:97,y:112));p.close();accent.setFill();p.fill();ink.setStroke();p.lineWidth=4;p.stroke();line([(64,96),(64,31),(56,24),(48,31)])
 case 11: line([(64,27),(23,63),(34,94),(55,103),(75,103),(99,91),(106,62)],ink,4,closed:true,fill:light);for x in [36,51,77,92]{line([(64,29),(CGFloat(x),91)],accent,3)}
 case 12: rect(23,34,82,61,accent);rect(32,54,64,29,light,5);oval(39,61,14,14,ink);oval(76,61,14,14,ink);line([(42,34),(48,46),(80,46),(86,34)],ink,3)
 case 13: oval(28,64,39,39,accent);oval(39,75,17,17,bg);line([(59,69),(95,31)],ink,8);line([(78,48),(87,57)],accent,8);line([(89,35),(101,46)],accent,8)
 case 14: oval(34,35,61,61,accent);let p=NSBezierPath(ovalIn:NSRect(x:17,y:48,width:96,height:29));ink.setStroke();p.lineWidth=5;p.stroke();oval(49,73,12,12,light);star(101,97,9,light)
 default: line([(64,104),(96,71),(64,37),(32,71)],ink,4,closed:true,fill:accent);line([(64,104),(64,37)],ink,3);line([(32,71),(96,71)],ink,3);line([(64,37),(55,25),(68,15)],ink,3)
 }
 // Distinct small orbit dots keep different fans of the same character recognizable.
 for d in 0...variant {oval(CGFloat(17+d*5),17+CGFloat(d%2)*4,4,4,accent)}
 NSGraphicsContext.restoreGraphicsState()
}
NSGraphicsContext.restoreGraphicsState()
try bitmap.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:output))
