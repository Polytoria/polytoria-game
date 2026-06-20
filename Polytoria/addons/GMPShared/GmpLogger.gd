#
# © 2026-present https://github.com/<<GitHubUsername>>
#

class_name GmpLogger extends Object


static func log_error(a_description: String) -> void:
	push_error("%s" % [a_description])


static func log_warn(a_description: String) -> void:
	push_warning("%s" % [a_description])


static func log_info(a_description: String) -> void:
	print_rich("[color=lime]INFO: %s[/color]" % [a_description])


static func error(a_plugin_name: String, a_description: String) -> void:
	log_error("%s: %s" % [a_plugin_name, a_description])


static func warn(a_plugin_name: String, a_description: String) -> void:
	log_warn("%s: %s" % [a_plugin_name, a_description])


static func info(a_plugin_name: String, a_description: String) -> void:
	log_info("%s: %s" % [a_plugin_name, a_description])
