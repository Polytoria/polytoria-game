#
# © 2024-present https://github.com/cengiz-pz
#

@tool
@icon("icon.png")
class_name Deeplink extends Node

signal deeplink_received(url: DeeplinkUrl)

const PLUGIN_SINGLETON_NAME: String = "DeeplinkPlugin"

const DEEPLINK_RECEIVED_SIGNAL_NAME = "deeplink_received"

enum Platform {
	Android = 1 << 0,  ## 1- Android
	iOS = 1 << 1,  ## 2- iOS
}

@export_category("Details")
## The part of the URL that identifies the protocol and the specific app to open, such as 'http' or a custom
## scheme like 'myapp'. It tells the operating system which application is responsible for handling the link and
## launching it, often leading to a specific screen or function within the app.
@export var scheme: String = "https"

## The host is the domain name of the server that provides the linked content, for example, example.com in
## https://example.com/app-page. It identifies the website or service the deeplink is connected to, allowing a
## device to route the user to the correct app.
@export var host: String = ""

## A path prefix in a deeplink is a specific part of a URL that is used to map a web link to a particular
## activity or screen within an app. For example, a path prefix like /recipe would route any URL with that
## prefix, such as http://www.recipe-app.com/recipe/grilled-potato-salad, directly to the recipe viewing screen
## for the specified recipe in the app instead of the website.
@export var path_prefix: String = ""

@export_category("Platform")
## List of platforms for which this deeplink will be exported.
@export_flags(" ") var enabled_platforms = Platform.Android | Platform.iOS:
	set = _set_enabled_platforms

@export_category("Android-specific")
@export_group("Intent", "android_")
## In Android, the android:label attribute within an <intent-filter> element serves to provide a user-readable
## label for the capabilities described by that specific intent filter. This label is displayed to the user
## when the activity is presented as an option to handle an intent that matches the filter.
@export var android_label: String = ""

## The android:autoVerify="true" attribute in an Android intent-filter is a crucial component for implementing
## Android App Links. It signals to the Android system that the app should be automatically verified as the
## default handler for specific web domains and schemes defined within the intent filter.
@export var android_is_auto_verify: bool = true

@export_group("Intent Category", "android_")
## The android.intent.category.DEFAULT category in an Android intent-filter indicates that the activity
## can be the target of an implicit intent when no other specific category is explicitly declared in the intent.
@export var android_is_default: bool = true

## The android.intent.category.BROWSABLE category in an Android intent-filter signifies that the target
## activity can be safely launched by a web browser or other applications that handle web links.
@export var android_is_browsable: bool = true

var _plugin_singleton: Object


func _ready() -> void:
	if _plugin_singleton == null:
		if Engine.has_singleton(PLUGIN_SINGLETON_NAME):
			_plugin_singleton = Engine.get_singleton(PLUGIN_SINGLETON_NAME)
			_connect_signals()
		elif not OS.has_feature("editor_hint"):
			GmpLogger.log_error("%s singleton not found!" % PLUGIN_SINGLETON_NAME)


func _validate_property(property: Dictionary) -> void:
	if property.name == "enabled_platforms":
		property.hint_string = ",".join(Platform.keys())
	elif property.name.begins_with("android_") and not is_platform_enabled(Platform.Android):
		property.usage = PROPERTY_USAGE_NONE


func _set_enabled_platforms(value: int) -> void:
	enabled_platforms = value
	notify_property_list_changed()


func _connect_signals() -> void:
	_plugin_singleton.connect(DEEPLINK_RECEIVED_SIGNAL_NAME, _on_deeplink_received)


func initialize() -> int:
	var __result = OK

	if _plugin_singleton != null:
		__result = _plugin_singleton.initialize()
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)

	return __result


func is_platform_enabled(a_platform: Platform) -> bool:
	return enabled_platforms & a_platform


func is_domain_associated(a_domain: String) -> bool:
	var __result = false
	if _plugin_singleton != null:
		__result = _plugin_singleton.is_domain_associated(a_domain)
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)

	return __result


func navigate_to_open_by_default_settings() -> void:
	if _plugin_singleton != null:
		_plugin_singleton.navigate_to_open_by_default_settings()
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)


func get_link_url() -> String:
	var __result = ""

	if _plugin_singleton != null:
		__result = _null_check(_plugin_singleton.get_url())
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)

	return __result


func get_link_scheme() -> String:
	var __result = ""

	if _plugin_singleton != null:
		__result = _null_check(_plugin_singleton.get_scheme())
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)

	return __result


func get_link_host() -> String:
	var __result = ""

	if _plugin_singleton != null:
		__result = _null_check(_plugin_singleton.get_host())
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)

	return __result


func get_link_path() -> String:
	var __result = ""

	if _plugin_singleton != null:
		__result = _null_check(_plugin_singleton.get_path())
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)

	return __result


func clear_data() -> void:
	if _plugin_singleton != null:
		_plugin_singleton.clear_data()
	else:
		GmpLogger.log_error("%s plugin not initialized" % PLUGIN_SINGLETON_NAME)


func _on_deeplink_received(a_data: Dictionary) -> void:
	deeplink_received.emit(DeeplinkUrl.new(a_data))


func _null_check(a_value) -> String:
	return "" if a_value == null else a_value
