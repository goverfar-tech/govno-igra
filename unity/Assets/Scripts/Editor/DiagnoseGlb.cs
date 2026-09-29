using System.IO;
using System.Text;
using GLTFast;
using GLTFast.Logging;
using UnityEditor;
using UnityEngine;

// Диагностика: грузим GLB напрямую через runtime-API glTFast
// (в обход ScriptedImporter) и печатаем ВСЕ сообщения логгера,
// чтобы увидеть настоящую причину, из-за которой молча падает
// штатный импорт «Failed to import (see inspector for details)».
public static class DiagnoseGlb
{
    [MenuItem("Survival/Diagnose GLB Import")]
    static async void Run()
    {
        string rel = "Assets/Models/tree.glb";
        var sb = new StringBuilder("[Diagnose] start\n");

        try
        {
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(rel));
            sb.AppendLine($"file bytes: {bytes.Length}");

            // UninterruptedDeferAgent — чтобы glTFast не создавал
            // служебный GameObject (DontDestroyOnLoad запрещён вне Play)
            var logger = new CollectingLogger();
            var gltf = new GltfImport(deferAgent: new UninterruptedDeferAgent(), logger: logger);
            bool ok = await gltf.LoadGltfBinary(bytes);
            sb.AppendLine($"runtime binary load ok={ok}");
            foreach (var item in logger.Items)
                sb.AppendLine($"  {item.Type} {item.Code}: {string.Join(" | ", item.Messages ?? new string[0])}");
        }
        catch (System.Exception e)
        {
            sb.AppendLine("EXCEPTION: " + e);
        }

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Survival", "Диагностика записана в Console/лог.", "Ок");
    }
}
