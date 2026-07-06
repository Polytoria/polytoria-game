@tool
extends EditorExportPlugin
class_name PolytoriaExeCpyExportPlugin

var exepaths = [
	"res://native/luau-lsp/",
	"res://native/stylua/",
]

func _get_name() -> String:
	return "pt_execpy"

func  _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
	if !features.has("creator"):
		return
	
	if !(features.has("windows") || features.has("linux") || features.has("macos")):
		return
	
	var export_dir := path.get_base_dir()
	print("Exporting to ", export_dir)
	
	var exesuffix = ""
	var exetarget = ""
	
	if features.has("linux"):
		exesuffix = ""
		exetarget = "linux"
	
	if features.has("macos"):
		exesuffix = ""
		exetarget = "macos"
	
	if features.has("windows"):
		exesuffix = ".exe"
		exetarget = "windows"
	
	for p in exepaths:
		var platformpath = p.path_join(exetarget)
		for item in DirAccess.get_files_at(platformpath):
			if item.ends_with(exesuffix):
				var fullpath = platformpath.path_join(item)
				print("Copying ", platformpath, " ", item)
				DirAccess.copy_absolute(ProjectSettings.globalize_path(fullpath), export_dir.path_join(item))
