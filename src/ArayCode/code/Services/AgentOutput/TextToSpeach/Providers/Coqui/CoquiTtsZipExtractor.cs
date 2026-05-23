using System;
using System.IO;

namespace ArayCode.TTS.Providers;

/// <summary>
/// Extracts .zip archives for Coqui TTS models (e.g. the "jenny" model)
/// that are distributed as ZIP archives requiring unpacking before loading.
///
/// <para>Separated from <see cref="CoquiTtsModelManager"/> to respect SRP:
/// model management and ZIP extraction are independent concerns.</para>
/// </summary>
public static class CoquiTtsZipExtractor
{
    /// <summary>
    /// Extracts any .zip archives found in the model's Coqui TTS storage directory.
    /// Deletes each ZIP after extraction. Skips __MACOSX and ._* junk from macOS ZIPs.
    /// Returns the number of archives successfully extracted.
    /// </summary>
    public static int ExtractModelZips(string modelName)
    {
        var modelDir = CoquiTtsModelManager.GetModelDir(modelName);
        if (modelDir == null)
            return 0;

        var extracted = 0;
        foreach (var zipPath in Directory.EnumerateFiles(modelDir, "*.zip"))
        {
            try
            {
                // Extract to a temp subdir first, then flatten up
                var tempDir = Path.Combine(modelDir, "__extract_tmp__");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);

                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempDir);
                File.Delete(zipPath);

                // Some model ZIPs (e.g. jenny) contain a nested folder.
                // Move files up to modelDir so TTS can find them.
                FlattenDir(tempDir, modelDir);
                Directory.Delete(tempDir, recursive: true);

                extracted++;
            }
            catch
            {
                // Extraction failed — leave the ZIP, model may still work
            }
        }
        return extracted;
    }

    /// <summary>
    /// Moves all files from <paramref name="sourceDir"/> into <paramref name="targetDir"/>,
    /// flattening any nested subdirectories. Skips __MACOSX and ._* junk from macOS ZIPs.
    /// </summary>
    private static void FlattenDir(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(file, dest);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (name == "__MACOSX" || name.StartsWith("._"))
            {
                try { Directory.Delete(subDir, recursive: true); } catch { }
                continue;
            }

            // If a dir with the same name already exists in target, merge into it
            var destSubDir = Path.Combine(targetDir, name);
            if (Directory.Exists(destSubDir))
            {
                // Move contents of source subdir into existing target subdir
                foreach (var f in Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(subDir, f);
                    var df = Path.Combine(destSubDir, rel);
                    var dp = Path.GetDirectoryName(df);
                    if (dp != null && !Directory.Exists(dp)) Directory.CreateDirectory(dp);
                    if (File.Exists(df)) File.Delete(df);
                    File.Move(f, df);
                }
                try { Directory.Delete(subDir, recursive: true); } catch { }
            }
            else
            {
                Directory.Move(subDir, destSubDir);
            }
        }
    }
}
