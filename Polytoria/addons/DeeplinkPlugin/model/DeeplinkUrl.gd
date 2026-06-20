#
# © 2024-present https://github.com/cengiz-pz
#

class_name DeeplinkUrl extends RefCounted

const SCHEME_PROPERTY := &"scheme"
const USER_PROPERTY := &"user"
const PASSWORD_PROPERTY := &"password"
const HOST_PROPERTY := &"host"
const PORT_PROPERTY := &"port"
const PATH_PROPERTY := &"path"
const PATH_EXTENSION_PROPERTY := &"path_extension"
const PATH_COMPONENTS_PROPERTY := &"path_components"
const QUERY_PROPERTY := &"query"
const FRAGMENT_PROPERTY := &"fragment"

var _data: Dictionary


func _init(a_data: Dictionary = {}):
	_data = {
		SCHEME_PROPERTY: a_data[SCHEME_PROPERTY] if a_data.has(SCHEME_PROPERTY) else "",
		USER_PROPERTY: a_data[USER_PROPERTY] if a_data.has(USER_PROPERTY) else "",
		PASSWORD_PROPERTY: a_data[PASSWORD_PROPERTY] if a_data.has(PASSWORD_PROPERTY) else "",
		HOST_PROPERTY: a_data[HOST_PROPERTY] if a_data.has(HOST_PROPERTY) else "",
		PORT_PROPERTY: a_data[PORT_PROPERTY] if a_data.has(PORT_PROPERTY) else -1,
		PATH_PROPERTY: a_data[PATH_PROPERTY] if a_data.has(PATH_PROPERTY) else "",
		PATH_EXTENSION_PROPERTY: a_data[PATH_EXTENSION_PROPERTY] if a_data.has(PATH_EXTENSION_PROPERTY) else "",
		PATH_COMPONENTS_PROPERTY: a_data[PATH_COMPONENTS_PROPERTY] if a_data.has(PATH_COMPONENTS_PROPERTY) else [],
		QUERY_PROPERTY: a_data[QUERY_PROPERTY] if a_data.has(QUERY_PROPERTY) else "",
		FRAGMENT_PROPERTY: a_data[FRAGMENT_PROPERTY] if a_data.has(FRAGMENT_PROPERTY) else ""
	}


func get_data() -> Dictionary:
	return _data


func get_scheme() -> String:
	return _data[SCHEME_PROPERTY] if _data[SCHEME_PROPERTY] else ""


func get_user() -> String:
	return _data[USER_PROPERTY] if _data[USER_PROPERTY] else ""


func get_password() -> String:
	return _data[PASSWORD_PROPERTY] if _data[PASSWORD_PROPERTY] else ""


func get_host() -> String:
	return _data[HOST_PROPERTY] if _data[HOST_PROPERTY] else ""


func get_port() -> int:
	return _data[PORT_PROPERTY] if _data[PORT_PROPERTY] else -1


func get_path() -> String:
	return _data[PATH_PROPERTY] if _data[PATH_PROPERTY] else ""


func get_path_extension() -> String:
	return _data[PATH_EXTENSION_PROPERTY] if _data[PATH_EXTENSION_PROPERTY] else ""


func get_path_components() -> Array:
	return _data[PATH_COMPONENTS_PROPERTY] if _data[PATH_COMPONENTS_PROPERTY] else []


func get_query() -> String:
	return _data[QUERY_PROPERTY] if _data[QUERY_PROPERTY] else ""


func get_fragment() -> String:
	return _data[FRAGMENT_PROPERTY] if _data[FRAGMENT_PROPERTY] else ""


func set_scheme(a_value: String) -> void:
	_data[SCHEME_PROPERTY] = a_value


func set_user(a_value: String) -> void:
	_data[USER_PROPERTY] = a_value


func set_password(a_value: String) -> void:
	_data[PASSWORD_PROPERTY] = a_value


func set_host(a_value: String) -> void:
	_data[HOST_PROPERTY] = a_value


func set_port(a_value: int) -> void:
	_data[PORT_PROPERTY] = a_value


func set_path(a_value: String) -> void:
	_data[PATH_PROPERTY] = a_value


func set_path_extension(a_value: String) -> void:
	_data[PATH_EXTENSION_PROPERTY] = a_value


func set_path_components(a_value: Array) -> void:
	_data[PATH_COMPONENTS_PROPERTY] = a_value


func set_query(a_value: String) -> void:
	_data[QUERY_PROPERTY] = a_value


func set_fragment(a_value: String) -> void:
	_data[FRAGMENT_PROPERTY] = a_value


func build_url() -> String:
	var url := ""

	# Scheme
	if _data.has(SCHEME_PROPERTY):
		var scheme: String = _data[SCHEME_PROPERTY]
		if scheme != "":
			url += scheme + "://"

	# User info
	var has_user: bool = _data.has(USER_PROPERTY) and _data[USER_PROPERTY] != ""
	var has_password: bool = _data.has(PASSWORD_PROPERTY) and _data[PASSWORD_PROPERTY] != ""

	if has_user:
		url += _data[USER_PROPERTY]
		if has_password:
			url += ":" + _data[PASSWORD_PROPERTY]
		url += "@"

	# Host
	if _data.has(HOST_PROPERTY):
		var host: String = _data[HOST_PROPERTY]
		if host != "":
			url += host

	# Port
	if _data.has(PORT_PROPERTY):
		var port = _data[PORT_PROPERTY]
		if typeof(port) == TYPE_INT and port > 0:
			url += ":" + str(port)

	# Path
	var path := ""

	# Path components
	if _data.has(PATH_COMPONENTS_PROPERTY):
		var components = _data[PATH_COMPONENTS_PROPERTY]
		if components is Array and not components.is_empty():
			path = "/" + "/".join(components)

	# Explicit path overrides components
	if _data.has(PATH_PROPERTY):
		var explicit_path: String = _data[PATH_PROPERTY]
		if explicit_path != "":
			if not explicit_path.begins_with("/"):
				explicit_path = "/" + explicit_path
			path = explicit_path

	# Path extension
	if path != "" and _data.has(PATH_EXTENSION_PROPERTY):
		var ext: String = _data[PATH_EXTENSION_PROPERTY]
		if ext != "":
			if not ext.begins_with("."):
				path += "."
			path += ext

	if path != "":
		url += path

	# Query (?query)
	if _data.has(QUERY_PROPERTY):
		var query: String = _data[QUERY_PROPERTY]
		if query != "":
			if not query.begins_with("?"):
				url += "?"
			url += query

	# Fragment (#fragment)
	if _data.has(FRAGMENT_PROPERTY):
		var fragment: String = _data[FRAGMENT_PROPERTY]
		if fragment != "":
			if not fragment.begins_with("#"):
				url += "#"
			url += fragment

	return url
