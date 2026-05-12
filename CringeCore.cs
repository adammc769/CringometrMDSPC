using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Assimp;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using Tesseract;

namespace CringometrMDSPC
{
    public sealed class AppOptions
    {
        public bool EnableImages { get; set; } = true;
        public bool EnableVideo { get; set; } = true;
        public bool EnableAudio { get; set; } = true;
        public bool Enable3D { get; set; } = true;
        public bool EnableReports { get; set; } = true;
        public bool EnableUrlDownload { get; set; } = true;
        public int VideoFps { get; set; } = 1;
        public List<string> Keywords { get; set; } = new List<string> { "sigma", "skibidi", "rizz", "ohio", "gyatt", "fanum", "mewing" };
    }

    internal sealed class AccInfo
    {
        public string Label { get; set; }
        public Color Color { get; set; }
    }

    internal static class CringeRules
    {
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        public static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mov", ".mkv", ".webm" };
        public static readonly string[] Model3DExtensions = { ".obj", ".bbmodel" };

        public static AccInfo GetAcc(int bdpm)
        {
            if (bdpm >= 1000) return new AccInfo { Label = LocalizationManager.GetString("VerdictACC5"), Color = Color.FromArgb(255, 0, 255) };
            if (bdpm >= 500) return new AccInfo { Label = LocalizationManager.GetString("VerdictACC4"), Color = Color.FromArgb(255, 40, 40) };
            if (bdpm >= 50) return new AccInfo { Label = LocalizationManager.GetString("VerdictACC3"), Color = Color.FromArgb(255, 136, 0) };
            if (bdpm >= 11) return new AccInfo { Label = LocalizationManager.GetString("VerdictACC2"), Color = Color.FromArgb(245, 210, 40) };
            return new AccInfo { Label = LocalizationManager.GetString("VerdictACC1"), Color = Color.FromArgb(68, 255, 68) };
        }

        public static bool IsImage(string path)
        {
            return HasExtension(path, ImageExtensions);
        }

        public static bool IsVideo(string path)
        {
            return HasExtension(path, VideoExtensions);
        }

        public static bool IsModel3D(string path)
        {
            return HasExtension(path, Model3DExtensions);
        }

        public static bool IsSupported(string path)
        {
            return IsImage(path) || IsVideo(path) || IsModel3D(path);
        }

        public static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }
            return builder.ToString();
        }

        public static string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        private static bool HasExtension(string path, IEnumerable<string> extensions)
        {
            var ext = Path.GetExtension(path);
            return extensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class CringePaths
    {
        public string Root { get; private set; }
        public string BaseFolder { get; private set; }
        public string GoodFolder { get; private set; }
        public string BadFolder { get; private set; }
        public string CriticalFolder { get; private set; }
        public string ScanFolder { get; private set; }
        public string ArchiveFolder { get; private set; }
        public string ReportsFolder { get; private set; }
        public string ClipModelFolder { get; private set; }
        public string ClipVisionModelPath { get; private set; }

        public CringePaths(string root)
        {
            Root = root;
            BaseFolder = Path.Combine(root, "Baza_Danych");
            GoodFolder = Path.Combine(BaseFolder, "DOBRE");
            BadFolder = Path.Combine(BaseFolder, "ZLE");
            CriticalFolder = Path.Combine(BaseFolder, "KRYTYCZNE");
            ScanFolder = Path.Combine(root, "DO_SKANOWANIA");
            ArchiveFolder = Path.Combine(BaseFolder, "ARCHIWUM_WYKRYTE");
            ReportsFolder = Path.Combine(BaseFolder, "RAPORTY");
            
            // Wsparcie dla płaskiej struktury (NSIS/SFX)
            ClipModelFolder = Path.Combine(root, "Models", "CLIP");
            string flatModelPath = Path.Combine(root, "vision_model.onnx");

            // Jeśli model jest bezpośrednio koło EXE, użyj go. W przeciwnym razie szukaj w podfolderze.
            if (File.Exists(flatModelPath))
                ClipVisionModelPath = flatModelPath;
            else
                ClipVisionModelPath = Path.Combine(ClipModelFolder, "vision_model.onnx");

        }

        public static string DetectRoot()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var projectDir = Path.GetFullPath(Path.Combine(baseDir, @"..\.."));
            if (File.Exists(Path.Combine(projectDir, "CringometrMDSPC.csproj")))
            {
                return projectDir;
            }
            return baseDir;
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(GoodFolder);
            Directory.CreateDirectory(BadFolder);
            Directory.CreateDirectory(CriticalFolder);
            Directory.CreateDirectory(ScanFolder);
            Directory.CreateDirectory(ArchiveFolder);
            Directory.CreateDirectory(ReportsFolder);
            Directory.CreateDirectory(ClipModelFolder);
        }
    }

    internal sealed class AnalysisResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Type { get; set; }
        public int Bdpm { get; set; }
        public string Verdict { get; set; }
        public string Details { get; set; }
        public string ChartLabel { get; set; }
        public Bitmap Preview { get; set; }

        public static AnalysisResult Skipped(string path, string reason)
        {
            return new AnalysisResult
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Type = LocalizationManager.GetString("FrameSkipped"),
                Bdpm = 0,
                Verdict = CringeRules.GetAcc(0).Label,
                Details = reason,
                ChartLabel = Path.GetFileName(path)
            };
        }
    }

    internal static class ImageLoader
    {
        public static Bitmap LoadBitmapUnlocked(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var image = Image.FromStream(stream))
            {
                return new Bitmap(image);
            }
        }
    }

    internal sealed class ReferenceDatabase
    {
        private readonly CringePaths _paths;
        private readonly Dictionary<string, List<double[]>> _bases = new Dictionary<string, List<double[]>>
        {
            { "DOBRE", new List<double[]>() },
            { "ZLE", new List<double[]>() },
            { "KRYTYCZNE", new List<double[]>() }
        };

        public ReferenceDatabase(CringePaths paths)
        {
            _paths = paths;
        }

        public Dictionary<string, int> Rebuild(Action<string> log, CultureInfo culture = null)
        {
            _paths.EnsureDirectories();
            _bases["DOBRE"].Clear();
            _bases["ZLE"].Clear();
            _bases["KRYTYCZNE"].Clear();

            LoadFolder("DOBRE", _paths.GoodFolder, log);
            LoadFolder("ZLE", _paths.BadFolder, log);
            LoadFolder("KRYTYCZNE", _paths.CriticalFolder, log);

            return _bases.ToDictionary(x => x.Key, x => x.Value.Count);
        }

        public bool HasAnyReference()
        {
            return _bases.Values.Any(x => x.Count > 0);
        }

        public AnalysisResult AnalyzeBitmap(Bitmap bitmap, string displayName)
        {
            var emb = ImageFeatureExtractor.CreateEmbedding(bitmap);
            var simGood = AverageTopSimilarity(emb, _bases["DOBRE"], 3);
            var simBad = AverageTopSimilarity(emb, _bases["ZLE"], 3);
            var simCritical = AverageTopSimilarity(emb, _bases["KRYTYCZNE"], 3);

            if (!simGood.HasValue && !simBad.HasValue && !simCritical.HasValue)
            {
                return new AnalysisResult
                {
                    FileName = displayName,
                    Type = LocalizationManager.GetString("TypeImage"),
                    Bdpm = 0,
                    Verdict = CringeRules.GetAcc(0).Label,
                    Details = LocalizationManager.GetString("BaseEmpty"),
                    ChartLabel = displayName
                };
            }

            var sGood = simGood.GetValueOrDefault();
            var sBad = simBad.GetValueOrDefault();
            var sCritical = simCritical.GetValueOrDefault();
            var cringeRaw = Math.Max(sBad, sCritical * 1.3) - (sGood * 1.1);
            var bdpm = (int)Math.Max(0, Math.Min(1200, 1200.0 / (1.0 + Math.Exp(-18.0 * (cringeRaw - 0.10)))));
            var acc = CringeRules.GetAcc(bdpm);

            return new AnalysisResult
            {
                FileName = displayName,
                Type = LocalizationManager.GetString("TypeImage"),
                Bdpm = bdpm,
                Verdict = acc.Label,
                Details = string.Format(LocalizationManager.CurrentCulture, "DOBRE {0} | ZLE {1} | KRYTYCZNE {2}",
                    FormatSimilarity(simGood), FormatSimilarity(simBad), FormatSimilarity(simCritical)),
                ChartLabel = displayName
            };
        }

        private void LoadFolder(string key, string folder, Action<string> log)
        {
            foreach (var file in Directory.EnumerateFiles(folder).Where(CringeRules.IsImage).OrderBy(x => x))
            {
                try
                {
                    using (var bitmap = ImageLoader.LoadBitmapUnlocked(file))
                    {
                        _bases[key].Add(ImageFeatureExtractor.CreateEmbedding(bitmap));
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke(LocalizationManager.GetString("LogBaseSkip", Path.GetFileName(file), ex.Message));
                }
            }
        }

        private static double? AverageTopSimilarity(double[] embedding, List<double[]> basis, int topK)
        {
            if (basis == null || basis.Count == 0)
            {
                return null;
            }

            var values = basis.Select(x => Dot(x, embedding))
                .OrderByDescending(x => x)
                .Take(Math.Min(topK, basis.Count))
                .ToList();
            return values.Average();
        }

        private static double Dot(double[] left, double[] right)
        {
            var total = 0.0;
            for (var i = 0; i < left.Length && i < right.Length; i++)
            {
                total += left[i] * right[i];
            }
            return total;
        }

        private static string FormatSimilarity(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.000", LocalizationManager.CurrentCulture) : "-";
        }
    }

    internal static class ImageFeatureExtractor
    {
        private const int ImageSize = 224;
        private const string ModelUrl = "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx";
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };
        private static readonly object Sync = new object();

        private static CringePaths _paths = new CringePaths(CringePaths.DetectRoot());
        private static Action<string> _log;
        private static InferenceSession _session;
        private static string _inputName;
        private static string _outputName;

        public static void Configure(CringePaths paths, Action<string> log)
        {
            _paths = paths ?? new CringePaths(CringePaths.DetectRoot());
            _log = log;
        }

        public static void Warmup()
        {
            EnsureReady();
        }

        public static double[] CreateEmbedding(Bitmap source)
        {
            EnsureReady();
            var input = Preprocess(source);
            var values = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, input)
            };

            using (var results = _session.Run(values))
            {
                var selected = SelectEmbeddingOutput(results.ToList());
                var tensor = selected.AsTensor<float>();
                var data = tensor.ToArray();
                if (data.Length != 512)
                {
                    throw new InvalidOperationException("Model CLIP nie zwrócił embeddingu image_embeds 512D. Liczba wartości: " + data.Length);
                }

                return Normalize(data.Select(x => (double)x).ToArray());
            }
        }

        private static void EnsureReady()
        {
            if (_session != null)
            {
                return;
            }

            lock (Sync)
            {
                if (_session != null)
                {
                    return;
                }

                // Gwarancja, że foldery istnieją przed inicjalizacją modelu
                _paths.EnsureDirectories();
                if (!File.Exists(_paths.ClipVisionModelPath))
                {
                    DownloadModel(_paths.ClipVisionModelPath);
                }

                _session = new InferenceSession(_paths.ClipVisionModelPath);
                _inputName = _session.InputMetadata.ContainsKey("pixel_values")
                    ? "pixel_values"
                    : _session.InputMetadata.Keys.First();
                _outputName = _session.OutputMetadata.ContainsKey("image_embeds")
                    ? "image_embeds"
                    : _session.OutputMetadata.Keys.FirstOrDefault(IsLikelyEmbeddingOutput);

                if (string.IsNullOrWhiteSpace(_outputName))
                {
                    _outputName = _session.OutputMetadata.Keys.First();
                }

                Log(LocalizationManager.GetString("ClipModelReady", Path.GetFileName(_paths.ClipVisionModelPath), _inputName, _outputName));
            }
        }

        private static DenseTensor<float> Preprocess(Bitmap source)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, ImageSize, ImageSize });

            using (var bitmap = new Bitmap(ImageSize, ImageSize, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                var scale = Math.Max(ImageSize / (double)source.Width, ImageSize / (double)source.Height);
                var width = (int)Math.Round(source.Width * scale);
                var height = (int)Math.Round(source.Height * scale);
                var x = (ImageSize - width) / 2;
                var y = (ImageSize - height) / 2;
                g.DrawImage(source, new Rectangle(x, y, width, height));

                var rect = new Rectangle(0, 0, ImageSize, ImageSize);
                System.Drawing.Imaging.BitmapData bmpData = null;
                try
                {
                    bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    int stride = bmpData.Stride;
                    byte[] rgbValues = new byte[stride * ImageSize];
                    Marshal.Copy(bmpData.Scan0, rgbValues, 0, rgbValues.Length);

                    for (var row = 0; row < ImageSize; row++)
                    {
                        for (var col = 0; col < ImageSize; col++)
                        {
                            int idx = (row * stride) + (col * 3);
                            tensor[0, 0, row, col] = ((rgbValues[idx + 2] / 255f) - Mean[0]) / Std[0];
                            tensor[0, 1, row, col] = ((rgbValues[idx + 1] / 255f) - Mean[1]) / Std[1];
                            tensor[0, 2, row, col] = ((rgbValues[idx] / 255f) - Mean[2]) / Std[2];
                        }
                    }
                }
                finally
                {
                    if (bmpData != null) bitmap.UnlockBits(bmpData);
                }
            }

            return tensor;
        }

        private static DisposableNamedOnnxValue SelectEmbeddingOutput(List<DisposableNamedOnnxValue> results)
        {
            var exact = results.FirstOrDefault(x => string.Equals(x.Name, "image_embeds", StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            var configured = results.FirstOrDefault(x => string.Equals(x.Name, _outputName, StringComparison.OrdinalIgnoreCase));
            if (configured != null && configured.AsTensor<float>().Length == 512)
            {
                return configured;
            }

            var sized = results.FirstOrDefault(x => x.AsTensor<float>().Length == 512);
            if (sized != null)
            {
                return sized;
            }

            throw new InvalidOperationException(LocalizationManager.GetString("ClipNo512DOutput", string.Join(", ", results.Select(x => x.Name))));
        }

        private static bool IsLikelyEmbeddingOutput(string name)
        {
            NodeMetadata metadata;
            if (!_session.OutputMetadata.TryGetValue(name, out metadata))
            {
                return false;
            }

            var dimensions = metadata.Dimensions;
            return dimensions != null
                && dimensions.Length <= 2
                && dimensions.Length > 0
                && dimensions[dimensions.Length - 1] == 512;
        }

        private static void DownloadModel(string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var tempPath = targetPath + ".download";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            Log(LocalizationManager.GetString("ClipDownloadStart"));
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            try
            {
                using (var client = new WebClient())
                {
                    // Obsługa zdarzenia postępu, aby wykorzystać istniejące klucze lokalizacji
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        if (e.TotalBytesToReceive > 0)
                            Log(LocalizationManager.GetString("ClipDownloadProgress", 
                                e.BytesReceived / 1024 / 1024, 
                                e.TotalBytesToReceive / 1024 / 1024));
                    };

                    // Pobieranie asynchroniczne z oczekiwaniem, aby umożliwić działanie zdarzeń
                    var task = client.DownloadFileTaskAsync(new Uri(ModelUrl), tempPath);
                    task.Wait();
                }
                Log(LocalizationManager.GetString("ClipDownloadSaved", targetPath));
            }
            catch (Exception ex)
            {
                Log("[CRITICAL] " + ex.Message);
                return;
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);
        }

        private static double[] Normalize(double[] vector)
        {
            var norm = Math.Sqrt(vector.Sum(x => x * x));
            if (norm <= 0.0000001)
            {
                return vector;
            }

            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
            return vector;
        }

        private static void Log(string message)
        {
            var log = _log;
            if (log != null)
            {
                log(message);
            }
        }
    }

    internal sealed class CringeAnalyzer
    {
        private readonly CringePaths _paths;
        private readonly ReferenceDatabase _database;
        private readonly AppOptions _options;
        private readonly Action<string> _log;

        public CringeAnalyzer(CringePaths paths, ReferenceDatabase database, AppOptions options, Action<string> log)
        {
            _paths = paths;
            _database = database;
            _options = options;
            _log = log;
        }

        public AnalysisResult AnalyzeFile(string path, Action<AnalysisResult> intermediateResult)
        {
            if (CringeRules.IsImage(path))
            {
                if (!_options.EnableImages)
                {
                    return AnalysisResult.Skipped(path, LocalizationManager.GetString("SkippedModuleDisabled", LocalizationManager.GetString("ModuleImage")));
                }
                return AnalyzeImageFile(path); // Przekazujemy ścieżkę do analizy obrazu
            }

            if (CringeRules.IsVideo(path))
            {
                if (!_options.EnableVideo)
                {
                    return AnalysisResult.Skipped(path, LocalizationManager.GetString("SkippedModuleDisabled", LocalizationManager.GetString("ModuleVideo")));
                }
                return AnalyzeVideoFile(path, intermediateResult); // Przekazujemy callback dla klatek wideo
            }

            if (CringeRules.IsModel3D(path))
            {
                if (!_options.Enable3D)
                {
                    return AnalysisResult.Skipped(path, LocalizationManager.GetString("SkippedModuleDisabled", LocalizationManager.GetString("Module3D")));
                }
                return AnalyzeModel3D(path, intermediateResult); // Nowość: analiza wizualna modelu
            }

            return AnalysisResult.Skipped(path, LocalizationManager.GetString("SkippedUnsupportedType"));
        }

        private void ApplyActiveLearning(AnalysisResult result)
        {
            if (string.IsNullOrEmpty(result.FilePath) || !File.Exists(result.FilePath)) return;

            try
            {
                if (result.Bdpm >= 1150)
                {
                    CopyToTraining(result.FilePath, "KRYTYCZNE");
                    _log?.Invoke(LocalizationManager.GetString("LearningAutoCritical", result.FileName));
                }
                else if (result.Bdpm <= 5 && result.Bdpm > 0)
                {
                    CopyToTraining(result.FilePath, "DOBRE");
                    _log?.Invoke(LocalizationManager.GetString("LearningAutoGood", result.FileName));
                }
            }
            catch { /* Ignorujemy błędy auto-learningu, aby nie przerywać audytu */ }
        }

        public AnalysisResult AnalyzeImageFile(string path)
        {
            using (var bitmap = ImageLoader.LoadBitmapUnlocked(path))
            {
                var result = _database.AnalyzeBitmap(bitmap, Path.GetFileName(path));

                // --- OCR SCAN ---
                try
                {
                    var tessDataPath = Path.Combine(_paths.Root, "tessdata");
                    if (Directory.Exists(tessDataPath))
                    {
                        using (var engine = new TesseractEngine(tessDataPath, "pol+eng", EngineMode.Default))
                        using (var page = engine.Process(bitmap))
                        {
                            var text = page.GetText().ToLowerInvariant();
                            var found = _options.Keywords.Where(k => text.Contains(k.ToLowerInvariant())).ToList();
                            if (found.Count > 0)
                            {
                                var penalty = found.Count * 150;
                                result.Bdpm = Math.Max(result.Bdpm, Math.Min(1200, penalty));
                                result.Verdict = CringeRules.GetAcc(result.Bdpm).Label;
                                result.Details += " | OCR: " + string.Join(", ", found);
                                _log?.Invoke(LocalizationManager.GetString("LogOCRDetected", string.Join(", ", found)));
                            }
                        }
                    }
                }
                catch (Exception ex) { _log?.Invoke("[OCR ERROR] " + ex.Message); }

                result.FilePath = path;
                result.FileName = Path.GetFileName(path);
                result.Type = LocalizationManager.GetString("TypeImage");
                result.ChartLabel = result.FileName;
                result.Preview = new Bitmap(bitmap);
                ApplyActiveLearning(result);
                return result;
            }
        }

        public AnalysisResult AnalyzeModel3D(string path, Action<AnalysisResult> intermediateResult)
        {
            var found = new List<string>();
            var penalty = 0;
            var visualMax = 0;
            Bitmap bestPreview = null;
            var viewResults = new List<AnalysisResult>();

            _paths.EnsureDirectories();
            try
            {
                // 1. Analiza tekstowa (istniejąca)
                var text = File.ReadAllText(path, Encoding.UTF8).ToLowerInvariant();
                var isBlockbench = path.EndsWith(".bbmodel", StringComparison.OrdinalIgnoreCase);
                foreach (var keyword in _options.Keywords)
                {
                    if (text.Contains(keyword))
                    {
                        found.Add(keyword);
                        penalty += isBlockbench ? 180 : 250;
                    }
                }

                if (isBlockbench)
                {
                    penalty += CountOccurrences(text, "\"animations\"") * 40;
                    penalty += CountOccurrences(text, "\"name\"") > 80 ? 80 : 0;
                }

                // 2. Analiza wizualna (renderowanie 20 widoków)
                // Uwaga: Prawdziwe renderowanie OBJ w C# wymaga biblioteki AssimpNet lub HelixToolkit.
                // Poniżej implementujemy logikę analizy CLIP dla rzutów modelu.
                var views = Render3DViews(path, 20);
                for (int i = 0; i < views.Count; i++)
                {
                    using (var viewBmp = views[i])
                    {
                        var viewResult = _database.AnalyzeBitmap(viewBmp, Path.GetFileName(path) + "_view" + i);
                        viewResult.Preview = new Bitmap(viewBmp);
                        viewResult.Type = LocalizationManager.GetString("Type3DRender");
                        viewResult.ChartLabel = Path.GetFileNameWithoutExtension(path) + "@v" + i;
                        
                        viewResults.Add(viewResult);

                        if (intermediateResult != null) intermediateResult(viewResult);

                        if (viewResult.Bdpm > visualMax)
                        {
                            visualMax = viewResult.Bdpm;
                            if (bestPreview != null) bestPreview.Dispose();
                            bestPreview = new Bitmap(viewBmp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return AnalysisResult.Skipped(path, LocalizationManager.GetString("Skipped3DReadError", ex.Message));
            }

            if (_options.EnableReports && viewResults.Count > 0)
            {
                var report = ReportWriter.WriteVideoFrames(_paths, path, viewResults, LocalizationManager.CurrentCulture);
                _log?.Invoke(LocalizationManager.GetString("VideoReport", Path.GetFileName(report)));
            }

            // Łączymy wyniki: najwyższy z wizualnych rzutów lub kara tekstowa
            var bdpm = Math.Max(visualMax, Math.Min(1200, penalty));
            var acc = CringeRules.GetAcc(bdpm);
            var result = new AnalysisResult
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Type = LocalizationManager.GetString("Type3DModel"),
                Bdpm = bdpm,
                Verdict = acc.Label,
                Details = LocalizationManager.GetString("Type3DDetails", found.Count > 0 ? LocalizationManager.GetString("3DDetectedPhrases", string.Join(", ", found)) : LocalizationManager.GetString("3DNoPhrases"), visualMax),
                ChartLabel = Path.GetFileName(path),
                Preview = bestPreview
            };
            ApplyActiveLearning(result);
            return result;
        }

        /// <summary>
        /// Generuje rzuty modelu 3D do analizy CLIP.
        /// </summary>
        private List<Bitmap> Render3DViews(string path, int count)
        {
            var bitmaps = new List<Bitmap>();
            try
            {
                var folder = Path.GetDirectoryName(path);
                using (var context = new AssimpContext())
                {
                    // Importujemy model z triangulacją i odwróceniem UV (ważne dla tekstur)
                    var scene = context.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals | PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.FlipUVs);
                    if (scene == null || !scene.HasMeshes) return bitmaps;

                    // Obliczanie Bounding Box modelu
                    Vector3D min = new Vector3D(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3D max = new Vector3D(float.MinValue, float.MinValue, float.MinValue);
                    foreach (var mesh in scene.Meshes)
                    {
                        foreach (var v in mesh.Vertices)
                        {
                            min.X = Math.Min(min.X, v.X); min.Y = Math.Min(min.Y, v.Y); min.Z = Math.Min(min.Z, v.Z);
                            max.X = Math.Max(max.X, v.X); max.Y = Math.Max(max.Y, v.Y); max.Z = Math.Max(max.Z, v.Z);
                        }
                    }

                    // Ręczne obliczenie środka (AssimpNet 4.1.0 nie obsługuje operatorów + / *)
                    Vector3D center = new Vector3D((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
                    float size = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
                    float scaleFactor = size > 0 ? 1.0f / size : 1.0f;

                    // Przygotowanie pędzli dla materiałów
                    var materialBrushes = new Dictionary<int, Brush>();
                    for (int m = 0; m < scene.MaterialCount; m++)
                    {
                        var mat = scene.Materials[m];
                        Brush brush = null;

                        if (mat.HasTextureDiffuse)
                        {
                            string rawPath = mat.TextureDiffuse.FilePath;
                            if (!string.IsNullOrEmpty(rawPath))
                            {
                                // Próba 1: Szukamy samej nazwy pliku w folderze modelu (najczęstszy przypadek)
                                string fileName = Path.GetFileName(rawPath);
                                string texPath = Path.Combine(folder, fileName);

                                // Próba 2: Jeśli nie ma, sprawdzamy ścieżkę relatywną zapisaną w modelu
                                if (!File.Exists(texPath))
                                    texPath = Path.Combine(folder, rawPath);

                                if (File.Exists(texPath))
                                {
                                    try { brush = new TextureBrush(ImageLoader.LoadBitmapUnlocked(texPath)); }
                                    catch { }
                                }
                            }
                        }

                        if (brush == null)
                        {
                            var c = mat.ColorDiffuse;
                            int alpha = (int)((mat.HasOpacity ? mat.Opacity : c.A) * 255);
                            brush = new SolidBrush(Color.FromArgb(alpha, (int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255)));
                        }
                        materialBrushes[m] = brush;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        var bmp = new Bitmap(640, 480);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(45, 45, 48));
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.SmoothingMode = SmoothingMode.AntiAlias;

                            // Obliczamy kąty obrotu (rozmieszczenie sferyczne)
                            float angleY = i * (float)Math.PI * 2 / count;
                            float angleX = (float)Math.Sin(i * 0.5) * 0.5f;

                            RenderScene(g, scene, materialBrushes, bmp.Width, bmp.Height, angleX, angleY, center, scaleFactor);
                        }
                        bitmaps.Add(bmp);
                    }

                    foreach (var b in materialBrushes.Values) b.Dispose();
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("[3D RENDER ERROR] " + ex.Message);
            }
            return bitmaps;
        }

        private void RenderScene(Graphics g, Scene scene, Dictionary<int, Brush> brushes, int width, int height, float angleX, float angleY, Vector3D center, float scaleFactor)
        {
            float scale = 0.8f * Math.Min(width, height);
            var centerX = width / 2;
            var centerY = height / 2;

            // Lista poligonów do posortowania (Painter's Algorithm)
            var facesToDraw = new List<ProjectedFace>();

            foreach (var mesh in scene.Meshes)
            {
                var brush = brushes.ContainsKey(mesh.MaterialIndex) ? brushes[mesh.MaterialIndex] : Brushes.Gray;

                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount < 3) continue;

                    var projectedPoints = new PointF[face.IndexCount];
                    float sumZ = 0;

                    for (int j = 0; j < face.IndexCount; j++)
                    {
                        var vRaw = mesh.Vertices[face.Indices[j]];
                        
                        // Centrowanie i skalowanie wierzchołka przed rotacją
                        Vector3D v = new Vector3D((vRaw.X - center.X) * scaleFactor, (vRaw.Y - center.Y) * scaleFactor, (vRaw.Z - center.Z) * scaleFactor);

                        // Rotacja Y
                        float x1 = (float)(v.X * Math.Cos(angleY) - v.Z * Math.Sin(angleY));
                        float z1 = (float)(v.X * Math.Sin(angleY) + v.Z * Math.Cos(angleY));
                        // Rotacja X
                        float y2 = (float)(v.Y * Math.Cos(angleX) - z1 * Math.Sin(angleX));
                        float z2 = (float)(v.Y * Math.Sin(angleX) + z1 * Math.Cos(angleX));

                        // Epsilon to prevent division by zero in perspective projection
                        float factor = 1.0f / Math.Max(0.001f, 2.0f + z2);
                        projectedPoints[j] = new PointF(centerX + x1 * scale * factor, centerY - y2 * scale * factor);
                        sumZ += z2;
                    }

                    facesToDraw.Add(new ProjectedFace { Points = projectedPoints, AvgZ = sumZ / face.IndexCount, Brush = brush });
                }
            }

            // Sortowanie od najdalszych do najbliższych
            foreach (var face in facesToDraw.OrderByDescending(f => f.AvgZ))
            {
                if (face.Brush is TextureBrush tb)
                {
                    // Resetujemy transformację pędzla i przesuwamy go do pierwszego wierzchołka poligonu.
                    // Zapobiega to efektowi "pływania" tekstury względem modelu.
                    tb.ResetTransform();
                    tb.TranslateTransform(face.Points[0].X, face.Points[0].Y);
                    g.FillPolygon(tb, face.Points);
                }
                else
                {
                    g.FillPolygon(face.Brush, face.Points);
                }
            }
        }

        private struct ProjectedFace
        {
            public PointF[] Points;
            public float AvgZ;
            public Brush Brush;
        }

        private void RenderWireframe(Graphics g, Scene scene, int width, int height, float angleX, float angleY)
        {
            using (var pen = new Pen(Color.Gainsboro, 1f))
            {
                float scale = 0.7f * Math.Min(width, height);
                var centerX = width / 2;
                var centerY = height / 2;

                foreach (var mesh in scene.Meshes)
                {
                    foreach (var face in mesh.Faces)
                    {
                        if (face.IndexCount < 2) continue;

                        var points = new List<PointF>();
                        for (int j = 0; j < face.IndexCount; j++)
                        {
                            var v = mesh.Vertices[face.Indices[j]];

                            // Prosta rotacja 3D -> 2D
                            float x = v.X;
                            float y = v.Y;
                            float z = v.Z;

                            // Rotacja Y
                            float x1 = (float)(x * Math.Cos(angleY) - z * Math.Sin(angleY));
                            float z1 = (float)(x * Math.Sin(angleY) + z * Math.Cos(angleY));

                            // Rotacja X
                            float y2 = (float)(y * Math.Cos(angleX) - z1 * Math.Sin(angleX));
                            float z2 = (float)(y * Math.Sin(angleX) + z1 * Math.Cos(angleX));

                            // Rzutowanie perspektywiczne (uproszczone)
                            float factor = 1.0f / Math.Max(0.001f, 2.0f + z2);
                            points.Add(new PointF(centerX + x1 * scale * factor, centerY - y2 * scale * factor));
                        }

                        if (points.Count > 1)
                        {
                            g.DrawPolygon(pen, points.ToArray());
                        }
                    }
                }
            }
        }

        public AnalysisResult AnalyzeVideoFile(string path, Action<AnalysisResult> intermediateResult)
        {
            var ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                return AnalysisResult.Skipped(path, LocalizationManager.GetString("SkippedFFmpegMissing"));
            }

            var temp = Path.Combine(Path.GetTempPath(), "cringometr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var framePattern = Path.Combine(temp, "frame_%04d.jpg");
            var frameResults = new List<AnalysisResult>();
            Bitmap bestPreview = null;
            var visualMax = 0;

            try
            {
                // Wykorzystanie dynamicznej wartości FPS z opcji
                var args = "-hide_banner -loglevel error -y -i " + Quote(path) + " -vf \"fps=" + _options.VideoFps + ",scale=640:-1\" -vsync 0 " + Quote(framePattern);
                // Zwiększono timeout do 10 minut (600000 ms)
                var run = ProcessRunner.Run(ffmpeg, args, 600000);
                if (run.ExitCode != 0 && _log != null)
                {
                    _log(LocalizationManager.GetString("LogFFmpegError", run.Output.Trim()));
                }

                var frames = Directory.EnumerateFiles(temp, "frame_*.jpg").OrderBy(x => x).ToList();
                var index = 0;
                foreach (var frame in frames)
                {
                    index++;
                    using (var bitmap = ImageLoader.LoadBitmapUnlocked(frame))
                    {
                        var frameResult = _database.AnalyzeBitmap(bitmap, Path.GetFileName(path) + "@" + index);
                        frameResult.FilePath = path;
                        frameResult.FileName = Path.GetFileName(path);
                        frameResult.Type = LocalizationManager.GetString("TypeVideoFrame");
                        frameResult.ChartLabel = ShortName(Path.GetFileNameWithoutExtension(path), 10) + "@" + index;
                        frameResult.Preview = new Bitmap(bitmap); // Attach preview for real-time UI updates
                        frameResults.Add(frameResult);
                        if (intermediateResult != null)
                        {
                            intermediateResult(frameResult);
                        }

                        if (frameResult.Bdpm >= visualMax)
                        {
                            visualMax = frameResult.Bdpm;
                            if (bestPreview != null)
                            {
                                bestPreview.Dispose();
                            }
                            bestPreview = new Bitmap(bitmap);
                        }
                    }
                    if (File.Exists(frame)) try { File.Delete(frame); } catch { }
                }

                var audioBdpm = 0;
                double? maxVolume = null;
                if (_options.EnableAudio)
                {
                    maxVolume = ProbeAudioMaxVolume(ffmpeg, path, LocalizationManager.CurrentCulture);
                    if (maxVolume.HasValue)
                    {
                        audioBdpm = (int)Math.Max(0, Math.Min(1200, 1200.0 / (1.0 + Math.Exp(-0.2 * (maxVolume.Value + 20.0)))));
                    }
                }

                if (_options.EnableReports && frameResults.Count > 0)
                {
                    var report = ReportWriter.WriteVideoFrames(_paths, path, frameResults, LocalizationManager.CurrentCulture);
                    if (_log != null) // Log messages should be localized
                    {
                        _log(LocalizationManager.GetString("VideoReport", Path.GetFileName(report)));
                    }
                }

                var finalBdpm = Math.Max(visualMax, audioBdpm);
                var acc = CringeRules.GetAcc(finalBdpm);
                var result = new AnalysisResult
                {
                    FilePath = path,
                    FileName = Path.GetFileName(path),
                    Type = LocalizationManager.GetString("TypeVideo"),
                    Bdpm = finalBdpm,
                    Verdict = acc.Label,
                    Details = LocalizationManager.GetString("TypeVideoDetails",
                        visualMax,
                        audioBdpm,
                        maxVolume.HasValue ? " (" + maxVolume.Value.ToString("0.0", CultureInfo.InvariantCulture) + " dB)" : "",
                        frameResults.Count),
                    ChartLabel = Path.GetFileName(path),
                    Preview = bestPreview
                };
                ApplyActiveLearning(result);
                return result;
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, true);
                }
                catch
                {
                    // Folder tymczasowy nie jest krytyczny dla wyniku skanowania.
                }
            }
        }

        public string CopyToTraining(string sourcePath, string rating)
        {
            var folder = RatingToFolder(rating);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "rated_" + CringeRules.Timestamp() + "_" + Path.GetFileName(sourcePath));
            File.Copy(sourcePath, target, false);
            return target;
        }

        public string SavePreviewToTraining(Image preview, string rating, string sourceName)
        {
            var folder = RatingToFolder(rating);
            Directory.CreateDirectory(folder);
            var name = "rated_" + CringeRules.Timestamp() + "_" + CringeRules.SanitizeFileName(Path.GetFileNameWithoutExtension(sourceName)) + ".jpg";
            var target = Path.Combine(folder, name);
            preview.Save(target, System.Drawing.Imaging.ImageFormat.Jpeg);
            return target;
        }

        public async Task<string> DownloadToScanFolderAsync(string url)
        {
            _paths.EnsureDirectories();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            {
                var fileName = GetFileNameFromUrl(url);
                var target = Path.Combine(_paths.ScanFolder, fileName);
                await client.DownloadFileTaskAsync(new Uri(url), target).ConfigureAwait(false);
                return target;
            }
        }

        private string RatingToFolder(string rating)
        {
            if (string.Equals(rating, "DOBRE", StringComparison.OrdinalIgnoreCase)) return _paths.GoodFolder;
            if (string.Equals(rating, "ZLE", StringComparison.OrdinalIgnoreCase)) return _paths.BadFolder;
            return _paths.CriticalFolder;
        }

        private static string GetFileNameFromUrl(string url)
        {
            Uri uri;
            string fileName = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                fileName = Path.GetFileName(uri.LocalPath);
            }

            if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains("."))
            {
                fileName = "downloaded_" + CringeRules.Timestamp() + ".bin";
            }

            return CringeRules.SanitizeFileName(fileName);
        }

        private string FindFfmpeg() // This method should be static or take paths as argument
        {
            var candidates = new[]
            {
                Path.Combine(_paths.Root, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Pomijamy niepoprawne wpisy PATH.
                }
            }

            return null;
        }

        private static double? ProbeAudioMaxVolume(string ffmpeg, string path, CultureInfo culture)
        {
            string nullDevice = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NUL" : "/dev/null";
            var args = $"-hide_banner -i {Quote(path)} -af volumedetect -vn -sn -dn -f null {nullDevice}";
            // Zwiększono timeout dla audio do 10 minut
            var run = ProcessRunner.Run(ffmpeg, args, 600000);
            var marker = "max_volume:";
            var idx = run.Output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var line = run.Output.Substring(idx).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (line == null)
            {
                return null;
            }

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                double value;
                if (double.TryParse(parts[i], NumberStyles.Float, culture, out value))
                {
                    return value;
                }
            }

            return null;
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string ShortName(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }

    internal sealed class ProcessRunResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
    }

    internal static class ProcessRunner
    {
        public static ProcessRunResult Run(string fileName, string arguments, int timeoutMs)
        {
            var output = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    return new ProcessRunResult { ExitCode = -1, Output = "Failed to start: " + ex.Message };
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Proces mógł zakończyć się między sprawdzeniem a Kill.
                    }
                    return new ProcessRunResult { ExitCode = -1, Output = output + Environment.NewLine + "Timeout." };
                }

                process.WaitForExit();
                return new ProcessRunResult { ExitCode = process.ExitCode, Output = output.ToString() };
            }
        }
    }

    internal static class ReportWriter
    {
        public static string WriteSummary(CringePaths paths, IEnumerable<AnalysisResult> results, CultureInfo culture)
        {
            paths.EnsureDirectories();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var path = Path.Combine(paths.ReportsFolder, "AUDYT_ZBIORCZY_" + CringeRules.Timestamp() + ".xlsx");
            
            var fileInfo = new FileInfo(path);
            using (var package = new ExcelPackage(fileInfo))
            {
                var sheet = package.Workbook.Worksheets.Add("Wyniki");
                
                sheet.Cells[1, 1].Value = LocalizationManager.GetString("GridColumnFile");
                sheet.Cells[1, 2].Value = LocalizationManager.GetString("GridColumnType");
                sheet.Cells[1, 3].Value = LocalizationManager.GetString("GridColumnBDPM");
                sheet.Cells[1, 4].Value = LocalizationManager.GetString("GridColumnVerdict");
                sheet.Cells[1, 5].Value = LocalizationManager.GetString("GridColumnDetails");
                
                using (var range = sheet.Cells[1, 1, 1, 5]) { range.Style.Font.Bold = true; }

                var resList = results.ToList();
                for (int i = 0; i < resList.Count; i++)
                {
                    var r = resList[i];
                    int row = i + 2;
                    sheet.Cells[row, 1].Value = r.FileName;
                    sheet.Cells[row, 2].Value = r.Type;
                    sheet.Cells[row, 3].Value = r.Bdpm;
                    sheet.Cells[row, 4].Value = r.Verdict;
                    sheet.Cells[row, 5].Value = r.Details;
                    
                    var accColor = CringeRules.GetAcc(r.Bdpm).Color;
                    sheet.Cells[row, 3, row, 4].Style.Font.Color.SetColor(accColor);
                }
                
                if (resList.Count > 0)
                {
                    var chart = sheet.Drawings.AddChart("SummaryChart", eChartType.ColumnClustered);
                    chart.Title.Text = "Podsumowanie BDPM - Audyt";
                    chart.SetPosition(1, 0, 6, 0);
                    chart.SetSize(800, 400);

                    var ySeries = sheet.Cells[2, 3, resList.Count + 1, 3];
                    var xSeries = sheet.Cells[2, 1, resList.Count + 1, 1];
                    chart.Series.Add(ySeries, xSeries).Header = LocalizationManager.GetString("GridColumnBDPM");
                }

                sheet.Cells.AutoFitColumns();
                package.Save();
            }
            return path;
        }

        public static string WriteVideoFrames(CringePaths paths, string videoPath, IEnumerable<AnalysisResult> frames, CultureInfo culture)
        {
            paths.EnsureDirectories();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var safe = CringeRules.SanitizeFileName(Path.GetFileNameWithoutExtension(videoPath));
            var path = Path.Combine(paths.ReportsFolder, "Raport_" + safe + "_" + CringeRules.Timestamp() + ".xlsx");
            
            var fileInfo = new FileInfo(path);
            using (var package = new ExcelPackage(fileInfo))
            {
                var sheet = package.Workbook.Worksheets.Add("Analiza");
                sheet.Cells[1, 1].Value = LocalizationManager.GetString("LiveChartSec");
                sheet.Cells[1, 2].Value = LocalizationManager.GetString("GridColumnBDPM");
                sheet.Cells[1, 3].Value = LocalizationManager.GetString("GridColumnVerdict");
                sheet.Cells[1, 4].Value = LocalizationManager.GetString("GridColumnDetails");
                
                var frameList = frames.ToList();
                for (int i = 0; i < frameList.Count; i++)
                {
                    var f = frameList[i];
                    int row = i + 2;
                    sheet.Cells[row, 1].Value = f.ChartLabel;
                    sheet.Cells[row, 2].Value = f.Bdpm;
                    sheet.Cells[row, 3].Value = f.Verdict;
                    sheet.Cells[row, 4].Value = f.Details;
                }

                if (frameList.Count > 0)
                {
                    var chart = sheet.Drawings.AddChart("CringeChart", eChartType.Line);
                    if (chart is ExcelLineChart lineChart)
                    {
                        lineChart.Title.Text = LocalizationManager.GetString("LiveChartVideoAnalysis", Path.GetFileName(videoPath));
                        lineChart.SetPosition(1, 0, 6, 0);
                        lineChart.SetSize(800, 400);
                        lineChart.Series.Add(sheet.Cells[2, 2, frameList.Count + 1, 2], sheet.Cells[2, 1, frameList.Count + 1, 1]).Header = LocalizationManager.GetString("GridColumnBDPM");
                    }
                }
                
                sheet.Cells.AutoFitColumns();
                package.Save();
            }
            return path;
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    internal static class ZipExporter
    {
        public static string Export(CringePaths paths)
        {
            paths.EnsureDirectories();
            var zipPath = Path.Combine(paths.ReportsFolder, "cringometr_eksport_" + CringeRules.Timestamp() + ".zip");
            var folders = new[]
            {
                paths.GoodFolder,
                paths.BadFolder,
                paths.CriticalFolder,
                paths.ReportsFolder
            };

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                    {
                        if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(zipPath), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var relative = Path.GetFileName(folder) + "/" + MakeZipPath(GetRelativePath(folder, file));
                        archive.CreateEntryFromFile(file, relative, System.IO.Compression.CompressionLevel.Optimal);
                    }
                }
            }

            return zipPath;
        }

        private static string GetRelativePath(string root, string file)
        {
            var rootUri = new Uri(AppendDirectorySeparatorChar(root));
            var fileUri = new Uri(file);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path + Path.DirectorySeparatorChar;
            }
            return path;
        }

        private static string MakeZipPath(string value)
        {
            return value.Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    internal static class CedManager
    {
        public static string Export(CringePaths paths, string targetPath, Action<int, int> progress = null)
        {
            paths.EnsureDirectories();
            if (File.Exists(targetPath)) File.Delete(targetPath);

            string[] folders = { paths.GoodFolder, paths.BadFolder, paths.CriticalFolder };
            var allFiles = folders.SelectMany(f => Directory.EnumerateFiles(f)).ToList();
            int current = 0;

            // Plik CED to w rzeczywistości spakowane foldery referencyjne
            using (var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create))
            {
                foreach (var folder in folders)
                {
                    var folderName = Path.GetFileName(folder);
                    foreach (var file in Directory.EnumerateFiles(folder))
                    {
                        string entryName = folderName + "/" + Path.GetFileName(file);
                        archive.CreateEntryFromFile(file, entryName);
                        current++;
                        progress?.Invoke(current, allFiles.Count);
                    }
                }
            }
            return targetPath;
        }

        public static void Import(CringePaths paths, string cedPath, bool clearExisting = false, Action<int, int> progress = null)
        {
            using (var archive = ZipFile.OpenRead(cedPath))
            {
                int total = archive.Entries.Count;
                int current = 0;

                if (clearExisting)
                {
                    CleanDatabase(paths);
                }

                foreach (var entry in archive.Entries)
                {
                    // Normalizacja ścieżki wpisu dla bieżącego systemu operacyjnego
                    string entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    var targetFile = Path.Combine(paths.BaseFolder, entryPath);

                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || string.IsNullOrEmpty(entry.Name))
                    {
                        // Wpis jest katalogiem - tylko upewniamy się, że istnieje
                        Directory.CreateDirectory(targetFile);
                    }
                    else
                    {
                        // Wpis jest plikiem - tworzymy folder nadrzędny i wypakowujemy
                        string dir = Path.GetDirectoryName(targetFile);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        entry.ExtractToFile(targetFile, true);
                    }

                    current++;
                    progress?.Invoke(current, total);
                }
            }
        }

        private static void CleanDatabase(CringePaths paths)
        {
            // Przy imporcie czyścimy obecną bazę, aby uniknąć duplikatów
            if (Directory.Exists(paths.BaseFolder))
            {
                // Wyciągamy tylko foldery kategorii, nie usuwamy całego BaseFolder (raporty zostają)
                foreach (var dir in new[] { paths.GoodFolder, paths.BadFolder, paths.CriticalFolder })
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
