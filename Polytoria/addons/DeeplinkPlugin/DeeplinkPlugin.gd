#
# © 2024-present https://github.com/cengiz-pz
#

@tool
extends EditorPlugin

const PLUGIN_NAME: String = "DeeplinkPlugin"
const ANDROID_DEPENDENCIES: Array = [ "androidx.annotation:annotation:1.9.1" ]
const IOS_PLATFORM_VERSION: String = "14.3"
const IOS_FRAMEWORKS: Array = [ "Foundation.framework", "AudioToolbox.framework" ]
const IOS_EMBEDDED_FRAMEWORKS: Array = [  ]
const IOS_LINKER_FLAGS: Array = [ "-ObjC" ]
const IOS_BUNDLE_FILES: Array = [  ]
const SPM_DEPENDENCIES: Array = [  ]

var android_export_plugin: AndroidExportPlugin
var ios_export_plugin: IosExportPlugin


func _enter_tree() -> void:
	android_export_plugin = AndroidExportPlugin.new()
	add_export_plugin(android_export_plugin)
	ios_export_plugin = IosExportPlugin.new()
	add_export_plugin(ios_export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(android_export_plugin)
	android_export_plugin = null
	remove_export_plugin(ios_export_plugin)
	ios_export_plugin = null


class AndroidExportPlugin extends EditorExportPlugin:
	var _plugin_name = PLUGIN_NAME
	var _export_config: DeeplinkExportConfig

	const DEEPLINK_ACTIVITY_FORMAT = """
		<activity
			android:name="org.godotengine.plugin.deeplink.DeeplinkActivity"
			android:theme="@android:style/Theme.Translucent.NoTitleBar.Fullscreen"
			android:excludeFromRecents="true"
			android:launchMode="singleTask"
			android:exported="true"
			android:noHistory="true">

			%s
		</activity>
"""

	const DEEPLINK_INTENT_FILTER_FORMAT = """
			<intent-filter android:label="%s" %s>
				<action android:name="android.intent.action.VIEW" />
				%s
				%s
				<data android:scheme="%s"
					android:host="%s"
					android:pathPrefix="%s" />
			</intent-filter>
"""

	const DEEPLINK_INTENT_FILTER_WITHOUT_HOST_FORMAT = """
			<intent-filter android:label="%s" %s>
				<action android:name="android.intent.action.VIEW" />
				%s
				%s
				<data android:scheme="%s"
					android:pathPrefix="%s" />
			</intent-filter>
"""

	const DEEPLINK_INTENT_FILTER_AUTO_VERIFY_PROPERTY = "android:autoVerify=\"true\""
	const DEEPLINK_INTENT_FILTER_DEFAULT_CATEGORY = "<category android:name=\"android.intent.category.DEFAULT\" />"
	const DEEPLINK_INTENT_FILTER_BROWSABLE_CATEGORY = "<category android:name=\"android.intent.category.BROWSABLE\" />"


	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformAndroid


	func _get_android_libraries(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		if debug:
			return PackedStringArray(["%s/bin/debug/%s-debug.aar" % [_plugin_name, _plugin_name]])
		else:
			return PackedStringArray(["%s/bin/release/%s-release.aar" % [_plugin_name, _plugin_name]])


	func _get_name() -> String:
		return _plugin_name


	func _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
		_export_config = DeeplinkExportConfig.new()
		if not _export_config.export_config_file_exists() or _export_config.load_export_config_from_file() != OK:
			_export_config.load_export_config_from_node(Deeplink.Platform.Android)


	func _get_android_dependencies(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		return PackedStringArray(ANDROID_DEPENDENCIES)


	func _get_android_manifest_application_element_contents(platform: EditorExportPlatform, debug: bool) -> String:
		var __filters: String = ""

		for __config in _export_config.deeplinks:
			if __config.host.is_empty():
				__filters += DEEPLINK_INTENT_FILTER_WITHOUT_HOST_FORMAT % [
							__config.label,
							DEEPLINK_INTENT_FILTER_AUTO_VERIFY_PROPERTY if __config.is_auto_verify else "",
							DEEPLINK_INTENT_FILTER_DEFAULT_CATEGORY if __config.is_default else "",
							DEEPLINK_INTENT_FILTER_BROWSABLE_CATEGORY if __config.is_browsable else "",
							__config.scheme,
							__config.path_prefix
						]
			else:
				__filters += DEEPLINK_INTENT_FILTER_FORMAT % [
							__config.label,
							DEEPLINK_INTENT_FILTER_AUTO_VERIFY_PROPERTY if __config.is_auto_verify else "",
							DEEPLINK_INTENT_FILTER_DEFAULT_CATEGORY if __config.is_default else "",
							DEEPLINK_INTENT_FILTER_BROWSABLE_CATEGORY if __config.is_browsable else "",
							__config.scheme,
							__config.host,
							__config.path_prefix
						]

		return DEEPLINK_ACTIVITY_FORMAT % __filters


class IosExportPlugin extends EditorExportPlugin:
	var _plugin_name = PLUGIN_NAME
	var _spm_dependencies = []
	var _export_path: String
	var _export_config: DeeplinkExportConfig

	const ENTITLEMENTS_FILE_HEADER := """<?xml version="1.0" encoding="UTF-8"?>
	<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
	<plist version="1.0">
	<dict>
		<key>com.apple.developer.associated-domains</key>
		<array>\n"""

	const ENTITLEMENTS_FILE_FOOTER := """\t\t</array>
	</dict>
	</plist>\n"""
	const EXPORT_FILE_SUFFIX := ".ipa"
	const ENTITLEMENTS_ARRAY_ITEM := "\t\t\t<string>applinks:%s</string>"

	const UNIVERSAL_LINK_SCHEMES := ["http", "https"]

	const CUSTOM_SCHEME_PLIST_ENTRY := """
	<key>CFBundleURLTypes</key>
	<array>
		<dict>
			<key>CFBundleURLName</key>
			<string>%s</string>
			<key>CFBundleURLSchemes</key>
			<array>%s
			</array>
		</dict>
	</array>
	"""

	const CUSTOM_SCHEME_ARRAY_ITEM := "\n\t\t\t\t<string>%s</string>"

	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformIOS


	func _get_name() -> String:
		return _plugin_name


	func _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
		# Expand manually-passed res:// or user:// paths to absolute paths
		_export_path = ProjectSettings.globalize_path(path)

		if _supports_platform(get_export_platform()):
			for __framework in IOS_FRAMEWORKS:
				add_apple_embedded_platform_framework(__framework)

			for __framework in IOS_EMBEDDED_FRAMEWORKS:
				add_apple_embedded_platform_embedded_framework(__framework)

			for __flag in IOS_LINKER_FLAGS:
				add_apple_embedded_platform_linker_flags(__flag)

			for __bundle_file in IOS_BUNDLE_FILES:
				add_apple_embedded_platform_bundle_file(__bundle_file)

			for __spm_dep in SPM_DEPENDENCIES:
				_spm_dependencies.append(SpmDependency.new(__spm_dep))

			_export_config = DeeplinkExportConfig.new()
			if not _export_config.export_config_file_exists() or _export_config.load_export_config_from_file() != OK:
				_export_config.load_export_config_from_node(Deeplink.Platform.iOS)

			# Compile a list of configured custom schemes
			var __custom_schemes: String = ""
			for __config in _export_config.deeplinks:
				if __config.scheme.to_lower() not in UNIVERSAL_LINK_SCHEMES:
					__custom_schemes += CUSTOM_SCHEME_ARRAY_ITEM % __config.scheme
					GmpLogger.info(PLUGIN_NAME, "Including custom scheme '%s'." % __config.scheme)

			# Add custom schemes to pList
			if not __custom_schemes.is_empty():
				var __entry := CUSTOM_SCHEME_PLIST_ENTRY % [get_option("application/bundle_identifier"), __custom_schemes]

				GmpLogger.info(PLUGIN_NAME, "Adding '%s' to plist." % __entry)

				add_apple_embedded_platform_plist_content(__entry)


	func _end_generate_apple_embedded_project(path: String, will_build_archive: bool) -> void:
		GmpLogger.info(PLUGIN_NAME, "Apple export project generated at: %s. Will build archive: %s"
				% [path, str(will_build_archive)])

		if _supports_platform(get_export_platform()):
			_spm_dependencies.append_array(_get_extra_dependencies())

			if _spm_dependencies.is_empty():
				GmpLogger.info(PLUGIN_NAME, "No SPM dependencies to install. Skipping.")
			else:
				GmpLogger.info(PLUGIN_NAME, "Installing %d SPM dependencies." % _spm_dependencies.size())
				_install_dependencies(path.get_base_dir(), path.get_file().get_basename())


	func _export_end() -> void:
		if _supports_platform(get_export_platform()):
			_regenerate_entitlements_file()


	func _get_extra_dependencies() -> Array[SpmDependency]:
		var __extra_dependencies:= [] as Array[SpmDependency]

		# Add any extra SPM dependencies here.

		return __extra_dependencies


	func _install_dependencies(a_base_dir: String, a_project_name: String) -> void:
		var __project_file_name:= "%s.xcodeproj" % a_project_name
		var __project_file_path:= a_base_dir.path_join(__project_file_name)
		if not DirAccess.dir_exists_absolute(__project_file_path):
			GmpLogger.error(PLUGIN_NAME, "Xcode project '%s' does not exist! Can't install SPM dependencies."
					% __project_file_path)
			return

		var __script_name = "add_dependency.rb"
		var __add_dependency_script_path = a_base_dir.path_join(__script_name)
		var __result = _generate_add_dependency_script(__add_dependency_script_path)
		if __result != Error.OK:
			GmpLogger.error(PLUGIN_NAME, "Failed to generate '%s' script with error %d!" % [__script_name, __result])
			return

		GmpLogger.info(PLUGIN_NAME, "Adding SPM dependencies to %s..." % __project_file_path)

		for __spm_dep: SpmDependency in _spm_dependencies:
			for __spm_dep_product: String in __spm_dep.get_products():
				var exec_output: Array = []
				var exec_code = OS.execute("ruby", [
							__add_dependency_script_path,
							__project_file_path,
							__spm_dep.get_url(),
							__spm_dep.get_version(),
							__spm_dep_product,
						], exec_output, true, false)

				if exec_code == 0:
					GmpLogger.info(PLUGIN_NAME, "Product %s for SPM dependency %s added successfully!"
							% [__spm_dep_product, __spm_dep.format_to_string()])
					for line in exec_output:
						GmpLogger.info(PLUGIN_NAME, "SPM: %s" % line)
				else:
					GmpLogger.info(PLUGIN_NAME, "Failed to add product %s for SPM dependency %s !"
							% [__spm_dep_product, __spm_dep.format_to_string()])
					for line in exec_output:
						GmpLogger.error(PLUGIN_NAME, "SPM: %s" % line)

		GmpLogger.info(PLUGIN_NAME, "Resolving SPM dependencies...")

		__script_name = "resolve_dependencies.sh"
		var __resolve_dependencies_script_path = a_base_dir.path_join(__script_name)
		__result = _generate_resolve_dependencies_script(__resolve_dependencies_script_path, a_base_dir, a_project_name)
		if __result != Error.OK:
			GmpLogger.error(PLUGIN_NAME, "Failed to generate '%s' script with error %d!" % [__script_name, __result])
			return

		var exec_output: Array = []
		var exec_code = OS.execute(__resolve_dependencies_script_path, [], exec_output, true, false)

		if exec_code == 0:
			for line in exec_output:
				GmpLogger.info(PLUGIN_NAME, "SPM: %s" % line)
			GmpLogger.info(PLUGIN_NAME, "Resolved dependencies successfully!")
		else:
			for line in exec_output:
				GmpLogger.error(PLUGIN_NAME, "SPM: %s" % line)
			GmpLogger.info(PLUGIN_NAME, "Failed to resolve dependencies! Try manually in Xcode.")


	const ADD_DEPENDENCY_RUBY_SCRIPT = """
require 'xcodeproj'

project_path = ARGV[0]
url          = ARGV[1].strip
version      = ARGV[2].strip
product_name = ARGV[3].strip

unless File.exist?(project_path)
	puts "Error: Xcode project not found at #{project_path}"
	exit 1
end

if url.empty? || version.empty? || product_name.empty?
	puts "Error: url, version, and product_name must all be non-empty."
	exit 1
end

begin
	project = Xcodeproj::Project.open(project_path)
	target = project.targets.first

	if target.nil?
		puts "Error: No targets found in the Xcode project."
		exit 1
	end

	existing_dep = target.package_product_dependencies.find do |dep|
		dep.product_name == product_name
	end

	if existing_dep
		puts "Warning: Product dependency '#{product_name}' already exists in the project. Skipping add.\n\n"
	else
		# Reuse an existing package reference for the same URL, or create a new one
		pkg = project.root_object.package_references.find do |p|
			p.repositoryURL == url
		end

		if pkg
			puts "Reusing existing package reference for '#{url}'."
		else
			pkg = project.new(Xcodeproj::Project::Object::XCRemoteSwiftPackageReference)
			pkg.repositoryURL = url
			pkg.requirement = {
				'kind' => 'upToNextMajorVersion',
				'minimumVersion' => version
			}
			project.root_object.package_references << pkg
		end

		# Create the product dependency and link it to the shared package reference
		ref = project.new(Xcodeproj::Project::Object::XCSwiftPackageProductDependency)
		ref.product_name = product_name
		ref.package = pkg
		target.package_product_dependencies << ref

		puts "Successfully added SPM dependency '#{product_name}' " \
				"(#{url} @ #{version}) to #{File.basename(project_path)}\n\n"
	end

	project.save

rescue => e
	puts "An error occurred: #{e.message}\n\n"
	exit 1
end
"""
	func _generate_add_dependency_script(a_script_path: String) -> Error:
		var __result = Error.OK

		var __script_content = ADD_DEPENDENCY_RUBY_SCRIPT

		__result = _create_script(a_script_path, __script_content)

		return __result


	const RESOLVE_DEPENDENCIES_BASH_SCRIPT = """
#!/bin/bash
set -e	# Exit on error

xcodebuild -resolvePackageDependencies \
			-project "%s.xcodeproj" \
			-scheme "%s"
"""
	func _generate_resolve_dependencies_script(a_script_path: String, a_base_dir: String,
			a_project_name: String) -> Error:
		var __result: Error = Error.OK

		var __script_content = RESOLVE_DEPENDENCIES_BASH_SCRIPT \
				% [ ProjectSettings.globalize_path(a_base_dir.path_join(a_project_name)), a_project_name ]

		__result = _create_script(a_script_path, __script_content)

		return __result


	func _create_script(a_script_path: String, a_script_content: String) -> Error:
		var __result: Error = Error.OK

		var __script_file = FileAccess.open(a_script_path, FileAccess.WRITE)
		if __script_file:
			__script_file.store_string(a_script_content)
			__script_file.close()
		else:
			__result = Error.ERR_FILE_CANT_WRITE

		var chmod_output: Array = []
		var chmod_code = OS.execute("chmod", ["+x", a_script_path], chmod_output, true, false)
		if chmod_code != 0:
			GmpLogger.error(PLUGIN_NAME, "Failed to chmod %s script: %s"
					% [a_script_path, (chmod_output if chmod_output.size() > 0 else "Unknown error")])
			__result = Error.ERR_FILE_NO_PERMISSION

		return __result


	func _regenerate_entitlements_file() -> void:
		if _export_path:
			if _export_path.ends_with(EXPORT_FILE_SUFFIX):
				var __project_path := ProjectSettings.globalize_path("res://")
				GmpLogger.info(PLUGIN_NAME, "******** PROJECT PATH='%s'" % __project_path)
				var __directory_path: String

				# Non-relative export paths should already be globalized to an absolute path at this point
				if _export_path.is_absolute_path():
					__directory_path = _export_path.trim_suffix(EXPORT_FILE_SUFFIX).simplify_path()
				else:
					# Handle relative export path by combining it with the project path and simplifying
					var __p := "%s%s" % [__project_path, _export_path.trim_suffix(EXPORT_FILE_SUFFIX)]
					__directory_path = __p.simplify_path()

				if not DirAccess.dir_exists_absolute(__directory_path):
					GmpLogger.warn(PLUGIN_NAME, "Creating non-existent export directory '%s'" % __directory_path)
					DirAccess.make_dir_recursive_absolute(__directory_path)

				var __project_name := _get_project_name_from_path(__directory_path)
				var __file_path := "%s/%s.entitlements" % [__directory_path, __project_name]
				GmpLogger.info(PLUGIN_NAME, "******** ENTITLEMENTS FILE PATH='%s'" % __file_path)
				if FileAccess.file_exists(__file_path):
					DirAccess.remove_absolute(__file_path)
				var __file = FileAccess.open(__file_path, FileAccess.WRITE)
				if __file:
					__file.store_string(ENTITLEMENTS_FILE_HEADER)

					for __config in _export_config.deeplinks:
						# As opposed to Android, in iOS __config.scheme, __config.path_prefix are
						# configured on the server side for Universal Links (apple-app-site-association file)
						var __entitlement := ENTITLEMENTS_ARRAY_ITEM % __config.host

						GmpLogger.info(PLUGIN_NAME, "Adding entitlement '%s'" % __entitlement)

						__file.store_line(__entitlement)

					__file.store_string(ENTITLEMENTS_FILE_FOOTER)
					__file.close()
				else:
					GmpLogger.error(PLUGIN_NAME, "Couldn't open file '%s' for writing." % __file_path)
			else:
				GmpLogger.error(PLUGIN_NAME, "Unexpected export path '%s'" % _export_path)
		else:
			GmpLogger.error(PLUGIN_NAME, "Export path is not defined.")


	func _get_project_name_from_path(a_path: String) -> String:
		var __result = ""

		var __split = a_path.rsplit("/", false, 1)
		if __split.size() > 1:
			__result = __split[1]

		return __result
