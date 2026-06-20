#
# © 2024-present https://github.com/cengiz-pz
#

class_name DeeplinkExportConfigItem extends RefCounted

var label: String
var is_auto_verify: bool
var is_default: bool
var is_browsable: bool
var scheme: String
var host: String
var path_prefix: String


func set_label(a_val: String) -> DeeplinkExportConfigItem:
	label = a_val
	return self


func set_is_auto_verify(a_val: bool) -> DeeplinkExportConfigItem:
	is_auto_verify = a_val
	return self


func set_is_default(a_val: bool) -> DeeplinkExportConfigItem:
	is_default = a_val
	return self


func set_is_browsable(a_val: bool) -> DeeplinkExportConfigItem:
	is_browsable = a_val
	return self


func set_scheme(a_val: String) -> DeeplinkExportConfigItem:
	scheme = a_val
	return self


func set_host(a_val: String) -> DeeplinkExportConfigItem:
	host = a_val
	return self


func set_path_prefix(a_val: String) -> DeeplinkExportConfigItem:
	path_prefix = a_val
	return self
