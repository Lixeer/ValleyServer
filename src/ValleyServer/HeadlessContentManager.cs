#pragma warning disable SYSLIB0050

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using StardewValley;

namespace HeadlessServer
{
    public class HeadlessContentManager : LocalizedContentManager
    {
        public static string ResolvedContentRoot { get; private set; } = "";

        public HeadlessContentManager(IServiceProvider serviceProvider, string rootDirectory)
            : base(serviceProvider, rootDirectory)
        {
            // Content is not part of the repository. Resolve it from an explicit setting
            // first, then from the published directory and common Steam locations. The old
            // implementation only checked ./Content, which made a normal Steam install
            // unusable unless files were manually copied beside the executable.
            string? configuredPath = Environment.GetEnvironmentVariable("VALLEY_CONTENT_PATH");
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string?[] possiblePaths = new[]
            {
                configuredPath,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content"),
                Path.Combine(Environment.CurrentDirectory, "Content"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Stardew Valley", "Content"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StardewValley", "Content"),
                Path.Combine(programFiles, "Steam", "steamapps", "common", "Stardew Valley", "Content"),
                Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Stardew Valley", "Content")
            };

            foreach (string? path in possiblePaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    _CachedContentRoot = Path.GetFullPath(path);
                    ResolvedContentRoot = _CachedContentRoot;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_CachedContentRoot))
            {
                // Keep the conventional path so the eventual load error names the expected
                // location, but report the actionable configuration knob to the operator.
                _CachedContentRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content"));
                ResolvedContentRoot = _CachedContentRoot;
                Console.WriteLine("[HeadlessContentManager] Content not found. Set VALLEY_CONTENT_PATH or install Content beside the server.");
            }

            Console.WriteLine($"[HeadlessContentManager] Content path resolved to: {_CachedContentRoot}");
        }

        public override LocalizedContentManager CreateTemporary()
        {
            return new HeadlessContentManager(base.ServiceProvider, base.RootDirectory);
        }

        private string ResolveAssetPath(string assetName)
        {
            string suffix = assetName;
            if (suffix.StartsWith("Content/", StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith("Content\\", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(8);
            }

            // Game asset names are Windows-style even when the server runs on Linux.
            // A backslash is a legal filename character on Unix, so Path.Combine alone
            // would look for e.g. "Content/Data\\Objects" instead of "Data/Objects.xnb".
            suffix = suffix.Replace('\\', Path.DirectorySeparatorChar)
                           .Replace('/', Path.DirectorySeparatorChar)
                           .TrimStart(Path.DirectorySeparatorChar);

            string path = Path.GetFullPath(Path.Combine(_CachedContentRoot, suffix));
            string root = Path.GetFullPath(_CachedContentRoot)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!path.StartsWith(root, pathComparison))
            {
                throw new ContentLoadException($"Asset path escapes the Content directory: '{assetName}'.");
            }

            if (!File.Exists(path) && File.Exists(path + ".xnb"))
            {
                path += ".xnb";
            }

            return path;
        }

        protected override Stream OpenStream(string assetName)
        {
            return File.OpenRead(ResolveAssetPath(assetName));
        }

        private static T CreateHeadlessAsset<T>()
        {
            object asset = FormatterServices.GetUninitializedObject(typeof(T));
            if (asset is Microsoft.Xna.Framework.Graphics.Texture2D texture)
            {
                // Texture2D.Bounds is queried by gameplay objects (e.g. Bush) even
                // when nothing is rendered. Give the inert texture valid dimensions.
                typeof(Microsoft.Xna.Framework.Graphics.Texture2D).GetField("width", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(texture, 1280);
                typeof(Microsoft.Xna.Framework.Graphics.Texture2D).GetField("height", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(texture, 1280);
            }
            return (T)asset;
        }

        public override T Load<T>(string assetName)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D))
            {
                return CreateHeadlessAsset<T>();
            }
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.SpriteFont));
            }
            return base.Load<T>(assetName);
        }

        public override T Load<T>(string assetName, LanguageCode language)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D))
            {
                return CreateHeadlessAsset<T>();
            }
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.SpriteFont));
            }
            return base.Load<T>(assetName, language);
        }

        public override T LoadImpl<T>(string baseAssetName, string localizedAssetName, LanguageCode languageCode)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D))
            {
                return CreateHeadlessAsset<T>();
            }
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.SpriteFont));
            }
            return base.LoadImpl<T>(baseAssetName, localizedAssetName, languageCode);
        }

        public override bool DoesAssetExist<T>(string assetName)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D) ||
                typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return true;
            }

            try
            {
                if (base.DoesAssetExist<T>(assetName))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                return File.Exists(ResolveAssetPath(assetName));
            }
            catch (ContentLoadException)
            {
                return false;
            }
        }
    }
}
