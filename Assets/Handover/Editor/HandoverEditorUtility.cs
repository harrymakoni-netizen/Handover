using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Handover.EditorTools
{
    /// <summary>Small Editor helpers shared by the scene builders.</summary>
    public static class HandoverEditorUtility
    {
        public const string ScenesFolder = "Assets/Handover/Content/Scenes";

        /// <summary>Adds (or moves) a scene in the build list; <paramref name="first"/> makes it the startup scene.</summary>
        public static void AddSceneToBuild(string scenePath, bool first)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == scenePath);
            var entry = new EditorBuildSettingsScene(scenePath, true);
            if (first)
            {
                scenes.Insert(0, entry);
            }
            else
            {
                scenes.Add(entry);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public static void EnsureFolder(string assetFolder)
        {
            Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
        }

        /// <summary>Sets a serialized field through SerializedObject so prefab overrides and saves are recorded.</summary>
        public static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[Handover] {target.GetType().Name} has no serialized field '{field}'.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[Handover] {target.GetType().Name} has no serialized field '{field}'.");
                return;
            }

            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
