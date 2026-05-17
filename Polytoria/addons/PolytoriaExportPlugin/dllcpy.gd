@tool
extends EditorExportPlugin
class_name PolytoriaDllCpyExportPlugin

## DLLCPY, Dll Copy. Used as a workaround for dll imports AOT bug

var dllpaths = [
	"res://native/discord/"
]

var all_platforms = ["linux", "windows", "macos"]

func _get_name() -> String:
	return "pt_dllcpy"

func  _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
	if !(features.has("windows") || features.has("linux") || features.has("macos")):
		return
	
	var export_dir := path.get_base_dir()
	print("Exporting to ", export_dir)
	
	var dllsuffix = ""
	var dlltarget = ""
	
	if features.has("linux"):
		dllsuffix = ".so"
		dlltarget = "linux"
	
	if features.has("windows"):
		dllsuffix = ".dll"
		dlltarget = "windows"
	
	if features.has("macos"):
		dllsuffix = ".dylib"
		dlltarget = "macos"
	
	# If exporting for server, copy all DLLs from all platforms
	if features.has("server"):
		for platform in all_platforms:
			for p in dllpaths:
				var platformpath = p.path_join(platform)
				if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(platformpath)):
					var target_subdir = export_dir.path_join("native").path_join(platform)
					DirAccess.make_dir_recursive_absolute(target_subdir)
					for item in DirAccess.get_files_at(platformpath):
						var fullpath = platformpath.path_join(item)
						var target_path = target_subdir.path_join(item)
						print("Copying (server) ", fullpath, " -> ", target_path)
						DirAccess.copy_absolute(ProjectSettings.globalize_path(fullpath), target_path)
	# Copy platform-specific DLLs
	for p in dllpaths:
		var platformpath = p.path_join(dlltarget)
		for item in DirAccess.get_files_at(platformpath):
			if item.ends_with(dllsuffix):
				var fullpath = platformpath.path_join(item)
				if features.has("macos"):
					add_shared_object(fullpath, PackedStringArray(), "")
				else:
					print("Copying ", platformpath, " ", item)
					DirAccess.copy_absolute(ProjectSettings.globalize_path(fullpath), export_dir.path_join(item))
