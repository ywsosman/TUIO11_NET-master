using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;
using System.Text;
using System.Threading;

namespace TuioDemoApp
{
    /// <summary>
    /// Runs <c>python/face_identification.py</c> which loads enrolment faces from
    /// <c>python/people/</c> via <c>people_face_login</c>. Parses MODE:, PERSON:, PROFILE:, optional AGE:.
    /// </summary>

    public static class FaceLogin
    {
        public enum LoginKind
        {
            Unknown = 0,
            Student,
            Teacher,
            Cancelled,
            Error
        }

        public class LoginResult
        {
            public LoginKind Kind = LoginKind.Unknown;
            public double? AgeYears;
            /// <summary>Face-match confidence 0–100 %. Populated when Python emits CONFIDENCE:.</summary>
            public double? Confidence;
            public string PersonDisplayName = "";
            public string ProfileKey = "";
            public string RawStdout = "";
            public string RawStderr = "";
            public string ErrorMessage = "";
        }

        /// <summary>
        /// Runs the Python identifier synchronously. Caller should run this on a background
        /// thread so the UI thread is not blocked while the camera analyses frames.
        /// Enrolment + dlib can take a short while on first import; generous default avoids
        /// false timeouts.
        /// </summary>
        /// <param name="timeoutSeconds">Hard cap for the whole call.</param>
        public static LoginResult Run(int timeoutSeconds = 180, CancellationToken cancellation = default(CancellationToken))
        {
            var result = new LoginResult();

            string scriptPath = ResolveScriptPath();
            if (string.IsNullOrEmpty(scriptPath))
            {
                result.Kind = LoginKind.Error;
                result.ErrorMessage = "face_identification.py not found in the python folder.";
                return result;
            }

            string pythonExe = ResolvePythonExecutable(Path.GetDirectoryName(scriptPath));
            // OpenCV highgui often fails to show a window when any standard stream is piped.
            // Protocol lines are written to --result-file by Python; do not redirect stdout or stderr.
            string resultPath = Path.Combine(Path.GetTempPath(), "tuio_face_login_result_" + Guid.NewGuid().ToString("N") + ".txt");
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-u \"" + scriptPath + "\" --result-file \"" + resultPath + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? "",
            };

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = psi;

                    process.Start();

                    int waited = 0;
                    int stepMs = 250;
                    int maxMs = Math.Max(1000, timeoutSeconds * 1000);
                    while (!process.HasExited && waited < maxMs)
                    {
                        if (cancellation.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            result.Kind = LoginKind.Cancelled;
                            break;
                        }
                        Thread.Sleep(stepMs);
                        waited += stepMs;
                    }
                    if (!process.HasExited)
                    {
                        try { process.Kill(); } catch { }
                        if (result.Kind == LoginKind.Unknown)
                        {
                            result.Kind = LoginKind.Error;
                            result.ErrorMessage = "Face login timed out (" + timeoutSeconds + "s).";
                        }
                    }
                    try { process.WaitForExit(5000); } catch { }
                }
            }
            catch (Exception ex)
            {
                result.Kind = LoginKind.Error;
                result.ErrorMessage = "Could not start python: " + ex.Message;
                result.RawStdout = "";
                result.RawStderr = "";
                return result;
            }

            try
            {
                if (File.Exists(resultPath))
                {
                    result.RawStdout = File.ReadAllText(resultPath, Encoding.UTF8);
                    try { File.Delete(resultPath); } catch { }
                }
                else
                {
                    result.RawStdout = "";
                }
            }
            catch
            {
                result.RawStdout = "";
            }
            result.RawStderr = "";

            // Don't override Cancelled set above.
            if (result.Kind == LoginKind.Unknown)
                ParseOutput(result, result.RawStdout);

            if (result.Kind == LoginKind.Error && string.IsNullOrEmpty(result.ErrorMessage))
                result.ErrorMessage = BuildFriendlyError(result.RawStderr, result.RawStdout, pythonExe);

            // Always write the full stderr/stdout to a log file so the user can
            // diagnose silent failures without re-running from a terminal.
            try { WriteDebugLog(result, pythonExe, scriptPath); } catch { }

            return result;
        }

        private static string BuildFriendlyError(string stderr, string stdout, string pythonExe)
        {
            string combined = ((stderr ?? "") + "\n" + (stdout ?? "")).ToLowerInvariant();
            if (combined.Contains("modulenotfounderror") || combined.Contains("no module named"))
            {
                if (combined.Contains("face_recognition"))
                    return "Python is missing 'face_recognition'. Run: " + pythonExe + " -m pip install face_recognition";
                if (combined.Contains("deepface"))
                    return "Python is missing 'deepface'. Run: " + pythonExe + " -m pip install deepface";
                if (combined.Contains("cv2") || combined.Contains("opencv"))
                    return "Python is missing 'opencv-python'. Run: " + pythonExe + " -m pip install opencv-python";
                if (combined.Contains("tensorflow"))
                    return "Python is missing 'tensorflow'. Install it in the same interpreter the app uses.";
                return "Python module missing. See face_login.log for details.";
            }
            if (combined.Contains("could not open camera") || combined.Contains("camera index out of range") || stdout != null && stdout.Contains("MODE:ERROR"))
                return "Camera busy or unavailable. Close other camera apps and retry.";
            if (string.IsNullOrWhiteSpace(stderr) && string.IsNullOrWhiteSpace(stdout))
                return "Python produced no output. Check that Python is on PATH and a webcam is connected.";
            return "Face identification failed. See face_login.log for the full Python error.";
        }

        private static void WriteDebugLog(LoginResult result, string pythonExe, string scriptPath)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TUIO_Evaluation");
            Directory.CreateDirectory(folder);
            string logPath = Path.Combine(folder, "face_login.log");
            var sb = new StringBuilder();
            sb.AppendLine("=== Face login attempt @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("Result: " + result.Kind +
                (result.Confidence.HasValue ? "  Confidence=" + result.Confidence.Value.ToString("0.0") + "%" : "") +
                (result.AgeYears.HasValue ? "  Age=" + result.AgeYears.Value.ToString("0.0") : "") +
                (string.IsNullOrEmpty(result.ProfileKey) ? "" : " Profile=" + result.ProfileKey));
            sb.AppendLine("Python: " + pythonExe);
            sb.AppendLine("Script: " + scriptPath);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                sb.AppendLine("Error : " + result.ErrorMessage);
            sb.AppendLine("--- stdout ---");
            sb.AppendLine(result.RawStdout ?? "");
            sb.AppendLine("--- stderr ---");
            sb.AppendLine(result.RawStderr ?? "");
            sb.AppendLine();
            File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
        }

        private static void ParseOutput(LoginResult result, string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                result.Kind = LoginKind.Error;
                return;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string modeLine = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("MODE:", StringComparison.OrdinalIgnoreCase))
                    modeLine = line.Substring("MODE:".Length).Trim();
                else if (line.StartsWith("PERSON:", StringComparison.OrdinalIgnoreCase))
                    result.PersonDisplayName = line.Substring("PERSON:".Length).Trim();
                else if (line.StartsWith("PROFILE:", StringComparison.OrdinalIgnoreCase))
                    result.ProfileKey = line.Substring("PROFILE:".Length).Trim();
                else if (line.StartsWith("AGE:", StringComparison.OrdinalIgnoreCase))
                {
                    string ageStr = line.Substring("AGE:".Length).Trim();
                    double age;
                    if (double.TryParse(ageStr, NumberStyles.Float, CultureInfo.InvariantCulture, out age))
                        result.AgeYears = age;
                }
                else if (line.StartsWith("CONFIDENCE:", StringComparison.OrdinalIgnoreCase))
                {
                    string confStr = line.Substring("CONFIDENCE:".Length).Trim();
                    double conf;
                    if (double.TryParse(confStr, NumberStyles.Float, CultureInfo.InvariantCulture, out conf))
                        result.Confidence = conf;
                }
                else if (line.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = line.Substring("ERR:".Length).Trim();
                }
            }

            if (string.IsNullOrEmpty(modeLine))
            {
                result.Kind = LoginKind.Error;
                return;
            }

            switch (modeLine.ToUpperInvariant())
            {
                case "GAME":
                    result.Kind = LoginKind.Student;
                    break;
                case "RADIAL":
                    result.Kind = LoginKind.Teacher;
                    break;
                case "CANCEL":
                    result.Kind = LoginKind.Cancelled;
                    break;
                case "ERROR":
                    result.Kind = LoginKind.Error;
                    break;
                default:
                    result.Kind = LoginKind.Error;
                    if (string.IsNullOrEmpty(result.ErrorMessage))
                        result.ErrorMessage = "Unrecognised MODE: " + modeLine;
                    break;
            }

            if (result.Kind != LoginKind.Student && result.Kind != LoginKind.Teacher)
                return;

            if (!string.IsNullOrEmpty(result.ProfileKey))
                return;

            bool teacher = result.Kind == LoginKind.Teacher;
            string slug = ProfileSlug(result.PersonDisplayName);
            result.ProfileKey = (teacher ? "teacher" : "student") +
                ":" + (string.IsNullOrEmpty(slug) ? "user" : slug);
        }

    public static string ProfileSlug(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = Regex.Replace(raw.Trim().ToLowerInvariant(), "&", " and ");
            raw = Regex.Replace(raw, @"[^a-z0-9]+", "-");
            raw = Regex.Replace(raw, "-{2,}", "-").Trim('-');
            return raw;
        }

        private static string ResolveScriptPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // The exe is usually in bin/Debug or bin/Release; walk up to find the python folder.
            string dir = baseDir;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "python", "face_identification.py");
                if (File.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static string ResolvePythonExecutable(string scriptDir)
        {
            // 1. Explicit override wins.
            string fromEnv = Environment.GetEnvironmentVariable("PYTHON");
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
                return fromEnv;

            // 2. Prefer a project-local virtualenv next to the script. This is by far
            //    the most common reason face-login "doesn't work": DeepFace is in the
            //    venv, but py.exe launches a different interpreter without it.
            string[] candidateDirs = new[]
            {
                scriptDir,
                scriptDir != null ? Path.GetDirectoryName(scriptDir) : null,
                AppDomain.CurrentDomain.BaseDirectory,
            };
            string[] venvSubpaths = new[]
            {
                Path.Combine(".venv", "Scripts", "python.exe"),
                Path.Combine("venv", "Scripts", "python.exe"),
                Path.Combine("env",  "Scripts", "python.exe"),
            };
            foreach (string dir in candidateDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                foreach (string sub in venvSubpaths)
                {
                    string candidate = Path.Combine(dir, sub);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // 3. py launcher is the most reliable on Windows when multiple system Pythons exist.
            if (PathHas("py.exe")) return "py";
            return "python";
        }

        private static bool PathHas(string exe)
        {
            try
            {
                string envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string p in envPath.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    try
                    {
                        if (File.Exists(Path.Combine(p, exe))) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }
    }
}
