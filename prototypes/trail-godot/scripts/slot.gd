extends Button
var index := 0
var hud
var drag_pending := false
var press_position := Vector2.ZERO

func _notification(what: int) -> void:
	if what == NOTIFICATION_DRAG_END or what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		drag_pending = false

func preview() -> Label:
	var node := Label.new()
	node.text = text
	node.mouse_filter = Control.MOUSE_FILTER_IGNORE
	node.add_theme_color_override("font_color", Color("fff4d8"))
	return node

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		drag_pending = event.pressed
		press_position = event.position
	if event is InputEventMouseMotion and drag_pending and not get_viewport().gui_is_dragging():
		if index < hud.game.s.bag.size() and event.position.distance_to(press_position) > 4:
			drag_pending = false
			force_drag({"slot": index}, preview())

func _get_drag_data(_at: Vector2) -> Variant:
	if index >= hud.game.s.bag.size():
		return null
	set_drag_preview(preview())
	return {"slot": index}

func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return data is Dictionary and data.has("slot") and index < hud.game.s.bag.size()

func _drop_data(_at: Vector2, data: Variant) -> void:
	# Rebuild the grid after the engine has finished dispatching the drop.
	hud.game.swap_slots.call_deferred(data.slot, index)
