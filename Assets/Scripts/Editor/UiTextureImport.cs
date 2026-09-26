using UnityEditor;
using UnityEngine;

namespace GrandStrategy.EditorTools
{
    /// <summary>
    /// Keeps interface images crisp. Flags, icons and UI textures are imported without mipmaps
    /// or compression: Unity's defaults blur small icons and add block artefacts to flags.
    /// </summary>
    sealed class UiTextureImport : AssetPostprocessor
    {
        static readonly string[] Folders =
        {
            "Assets/Resources/Flags/",
            "Assets/Resources/Icons/",
            "Assets/Resources/UI/",
        };

        void OnPreprocessTexture()
        {
            if (!IsUiTexture(assetPath))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
        }

        static bool IsUiTexture(string path)
        {
            foreach (var folder in Folders)
                if (path.StartsWith(folder, System.StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
