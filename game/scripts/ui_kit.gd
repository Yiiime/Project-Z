extends RefCounted
## C.02 shared visual tokens. Presentation only; no world state.
const INK := Color("101c2b")
const WHITE := Color("f2f3f5")
const MUTED := Color("b5c1d0")
const PALE := Color("c5d9ec")
const LINE := Color(0.847,0.898,0.965,0.16)
static var _fonts: Dictionary = {}

static func font(weight: float=450.0, digits: bool=false) -> Font:
	var key := str(weight)+str(digits)
	if _fonts.has(key): return _fonts[key]
	var result := FontVariation.new()
	result.base_font=load("res://assets/fonts/Manrope-Variable.ttf" if digits else "res://assets/fonts/NotoSansSC-Variable.ttf")
	result.variation_opentype={TextServerManager.get_primary_interface().name_to_tag("wght"):weight}
	if digits: result.fallbacks=[font(weight)]
	_fonts[key]=result
	return result

static func icon(name: String) -> Texture2D:
	return load("res://assets/ui/icons/%s.svg" % name) as Texture2D

static func style(fill: Color, border: Color=Color.TRANSPARENT, radius: int=6) -> StyleBoxFlat:
	var s:=StyleBoxFlat.new()
	s.bg_color=fill; s.border_color=border
	s.set_border_width_all(1 if border.a>0 else 0)
	s.set_corner_radius_all(radius)
	s.content_margin_left=18; s.content_margin_right=18
	s.content_margin_top=12; s.content_margin_bottom=12
	return s

static func make_theme() -> Theme:
	var t:=Theme.new()
	t.default_font=font(); t.default_font_size=19
	t.set_color("font_color","Label",WHITE)
	t.set_constant("line_spacing","Label",7)
	t.set_color("font_color","Button",WHITE)
	t.set_color("font_hover_color","Button",WHITE)
	t.set_color("font_focus_color","Button",WHITE)
	t.set_color("font_pressed_color","Button",WHITE)
	t.set_color("font_disabled_color","Button",Color(0.67,0.73,0.8,0.6))
	t.set_constant("h_separation","Button",12)
	t.set_constant("icon_max_width","Button",24)
	t.set_stylebox("normal","Button",style(Color(0.77,0.85,0.93,0.035),Color(0.85,0.9,0.96,0.08),4))
	t.set_stylebox("hover","Button",style(Color(0.77,0.85,0.93,0.105),Color(0.85,0.9,0.96,0.2),4))
	t.set_stylebox("pressed","Button",style(Color(0.77,0.85,0.93,0.17),Color(0.85,0.9,0.96,0.3),4))
	t.set_stylebox("disabled","Button",style(Color.TRANSPARENT,Color(0.85,0.9,0.96,0.045),4))
	t.set_stylebox("focus","Button",style(Color.TRANSPARENT,PALE,4))
	for n in ["VScrollBar","HScrollBar"]:
		var track:=style(Color.TRANSPARENT); track.content_margin_left=3; track.content_margin_right=3
		t.set_stylebox("scroll",n,track)
		for state in ["grabber","grabber_highlight","grabber_pressed"]:
			var thumb:=style(Color(0.68,0.77,0.86,0.30),Color.TRANSPARENT,2)
			thumb.content_margin_left=3; thumb.content_margin_right=3
			t.set_stylebox(state,n,thumb)
		for state in ["increment","increment_highlight","increment_pressed","decrement","decrement_highlight","decrement_pressed"]:
			t.set_icon(state,n,ImageTexture.new())
	return t

static func primary(b: Button) -> void:
	for n in ["normal","hover","pressed"]:
		b.add_theme_stylebox_override(n,style(PALE if n=="normal" else Color("e0ebf4"),Color.TRANSPARENT,5))
	for n in ["font_color","font_hover_color","font_focus_color","font_pressed_color"]:
		b.add_theme_color_override(n,INK)
	b.add_theme_font_override("font",font(550.0))

static func quiet(b: Button) -> void:
	b.add_theme_stylebox_override("normal",style(Color.TRANSPARENT,Color.TRANSPARENT,4))
	b.add_theme_stylebox_override("disabled",style(Color.TRANSPARENT,Color.TRANSPARENT,4))

static func compact(b: Button) -> void:
	for state in ["normal","hover","pressed","disabled","focus"]:
		var s: StyleBoxFlat=b.get_theme_stylebox(state).duplicate()
		s.content_margin_left=6; s.content_margin_right=6
		s.content_margin_top=4; s.content_margin_bottom=4
		b.add_theme_stylebox_override(state,s)

static func divider(vertical: bool=false) -> ColorRect:
	var line:=ColorRect.new(); line.color=LINE; line.mouse_filter=Control.MOUSE_FILTER_IGNORE
	line.custom_minimum_size=Vector2(1,0) if vertical else Vector2(0,1)
	if vertical: line.size_flags_vertical=Control.SIZE_EXPAND_FILL
	return line

static func picture(texture: Texture2D, bounds: Vector2) -> TextureRect:
	var p:=TextureRect.new(); p.texture=texture; p.custom_minimum_size=bounds
	p.expand_mode=TextureRect.EXPAND_IGNORE_SIZE; p.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	p.mouse_filter=Control.MOUSE_FILTER_IGNORE
	return p

static func spacer(height: float=16) -> Control:
	var c:=Control.new(); c.custom_minimum_size.y=height; c.mouse_filter=Control.MOUSE_FILTER_IGNORE
	return c

static func fade(from: Color, to: Color, bounds: Rect2, horizontal: bool=false) -> TextureRect:
	var g:=Gradient.new(); g.colors=PackedColorArray([from,to])
	var texture:=GradientTexture2D.new(); texture.gradient=g; texture.fill_from=Vector2.ZERO; texture.fill_to=Vector2.RIGHT if horizontal else Vector2.DOWN
	var p:=picture(texture,Vector2.ZERO); p.stretch_mode=TextureRect.STRETCH_SCALE; p.position=bounds.position; p.size=bounds.size
	return p
