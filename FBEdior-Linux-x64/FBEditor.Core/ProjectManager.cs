using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FBEditor.Core;

/// <summary>
/// Project-level settings: project type (Console/GUI/Window9), Window9 paths,
/// source/design files. Serialized to .fbproj JSON.
/// Public fields (not properties) preserve the JSON key names from the VB version.
/// </summary>
public class ProjectSettings
{
    public string ProjectName = "Untitled";
    public ProjectType ProjectType = ProjectType.ConsoleApp;
    public string Window9IncludePath = "";
    public string Window9LibPath = "";
    public string MainSourceFile = "";
    public List<string> SourceFiles = new();
    public string FormDesignFile = "";     // .w9form JSON file
    public string GeneratedCodeFile = "";  // Auto-generated .bas from designer
    public DateTime LastModified = DateTime.Now;
}

/// <summary>
/// Manages the current project and all .fbproj / .w9form load+save.
/// Ported from Modules/ProjectManager.vb (VB Module -> C# static class).
/// </summary>
public static class ProjectManager
{
    private static ProjectSettings? _currentProject;
    private static string _projectFilePath = "";
    private static bool _isDirty;

    public static ProjectSettings CurrentProject
    {
        get
        {
            _currentProject ??= new ProjectSettings();
            return _currentProject;
        }
    }

    public static string ProjectFilePath => _projectFilePath;

    public static bool IsDirty => _isDirty;

    public static bool IsWindow9Project =>
        CurrentProject.ProjectType == ProjectType.Window9FormsApp;

    public static bool IsGUIProject =>
        CurrentProject.ProjectType == ProjectType.GUIApp ||
        CurrentProject.ProjectType == ProjectType.Window9FormsApp;

    public static void MarkDirty() => _isDirty = true;

    /// <summary>Create a new project with the specified type.</summary>
    public static void NewProject(ProjectType projType, string projName = "Untitled")
    {
        _currentProject = new ProjectSettings
        {
            ProjectName = projName,
            ProjectType = projType,
            LastModified = DateTime.Now
        };
        _projectFilePath = "";
        _isDirty = true;
    }

    /// <summary>Save project to a .fbproj JSON file.</summary>
    public static bool SaveProject(string filePath)
    {
        try
        {
            CurrentProject.LastModified = DateTime.Now;
            var json = JsonConvert.SerializeObject(CurrentProject, Formatting.Indented);
            File.WriteAllText(filePath, json);
            _projectFilePath = filePath;
            _isDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to save project: {filePath}", ex);
            return false;
        }
    }

    /// <summary>Load project from a .fbproj JSON file.</summary>
    public static bool LoadProject(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var json = File.ReadAllText(filePath);
            _currentProject = JsonConvert.DeserializeObject<ProjectSettings>(json);
            _projectFilePath = filePath;
            _isDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to load project: {filePath}", ex);
            return false;
        }
    }

    /// <summary>Get extra FBC flags needed for the current project type.</summary>
    public static string GetProjectCompilerFlags()
    {
        switch (CurrentProject.ProjectType)
        {
            case ProjectType.GUIApp:
                return "-s gui";
            case ProjectType.Window9FormsApp:
                var flags = "-s gui";
                if (!string.IsNullOrEmpty(CurrentProject.Window9IncludePath))
                    flags += " -i \"" + CurrentProject.Window9IncludePath + "\"";
                if (!string.IsNullOrEmpty(CurrentProject.Window9LibPath))
                    flags += " -p \"" + CurrentProject.Window9LibPath + "\"";
                return flags;
            default:
                return "";
        }
    }

    /// <summary>Try to auto-detect Window9 installation by checking common paths.</summary>
    public static string AutoDetectWindow9Path()
    {
        // Re-enabled now that AppGlobals.Build.FBCPath exists (was stubbed before AppSettings landed).
        var fbcPath = AppGlobals.Build.FBCPath;
        if (!string.IsNullOrEmpty(fbcPath))
        {
            var fbcDir = Path.GetDirectoryName(fbcPath);
            if (!string.IsNullOrEmpty(fbcDir))
            {
                // Check fbc/inc directory
                var incDir = Path.Combine(fbcDir, "inc");
                if (File.Exists(Path.Combine(incDir, "window9.bi"))) return incDir;
                // Check parent/inc
                var parent = Path.GetDirectoryName(fbcDir);
                if (!string.IsNullOrEmpty(parent))
                {
                    var parentInc = Path.Combine(parent, "inc");
                    if (File.Exists(Path.Combine(parentInc, "window9.bi"))) return parentInc;
                }
            }
        }
        return "";
    }

    /// <summary>Save form design to a .w9form JSON file alongside the project.</summary>
    public static bool SaveFormDesign(W9FormDesign design, string filePath)
    {
        try
        {
            var json = JsonConvert.SerializeObject(design, Formatting.Indented);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to save form design: {filePath}", ex);
            return false;
        }
    }

    /// <summary>Load form design from a .w9form JSON file.</summary>
    public static W9FormDesign? LoadFormDesign(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<W9FormDesign>(json);
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to load form design: {filePath}", ex);
            return null;
        }
    }

    /// <summary>Save a multi-form project to a .w9form JSON file.</summary>
    public static bool SaveFormProject(W9FormProject proj, string filePath)
    {
        try
        {
            var json = JsonConvert.SerializeObject(proj, Formatting.Indented);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to save form project: {filePath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Load a multi-form project from a .w9form JSON file.
    /// Backward compatible: if the file contains a single W9FormDesign (old format),
    /// it wraps it into a W9FormProject automatically.
    /// </summary>
    public static W9FormProject? LoadFormProject(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var json = File.ReadAllText(filePath);

            // Decide the format by inspecting the JSON shape rather than relying on a
            // try/deserialize that always "succeeds": W9FormProject's constructor seeds
            // a default main form, so deserializing single-form JSON into it wrongly
            // yields a project containing one EMPTY form (Forms.Count > 0 passes and the
            // single-form fallback never runs). Checking for a "Forms" array is
            // unambiguous. NOTE: this fixes a latent bug carried over from the VB
            // original, where the single-form (old-format) fallback was unreachable.
            var root = JObject.Parse(json);

            if (root["Forms"] is JArray)
            {
                // New format: a W9FormProject.
                // Use Replace mode so the constructor's default Forms list gets replaced
                // instead of appended to by the deserializer.
                var settings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                var proj = JsonConvert.DeserializeObject<W9FormProject>(json, settings);
                if (proj != null && proj.Forms != null && proj.Forms.Count > 0)
                    return proj;
            }

            // Old format (single W9FormDesign, no "Forms" array) -> wrap in a project.
            var singleForm = JsonConvert.DeserializeObject<W9FormDesign>(json);
            if (singleForm != null)
            {
                var wrap = new W9FormProject();
                wrap.Forms.Clear();
                singleForm.FormType = W9FormType.MainForm;
                if (string.IsNullOrEmpty(singleForm.VarName)) singleForm.VarName = "hMainForm";
                wrap.Forms.Add(singleForm);
                return wrap;
            }

            return null;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to load form project: {filePath}", ex);
            return null;
        }
    }
}
