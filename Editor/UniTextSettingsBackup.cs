using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class UniTextSettingsBackup
    {
        private const string BackupDir = "ProjectSettings/UniText";
        private const string BackupPath = "ProjectSettings/UniText/Settings.json";

        private static readonly string[] Fields = { "gradients", "defaultFontStack" };

        [Serializable]
        private class Data
        {
            public string gradients = "";
            public string defaultFontStack = "";
        }

        public static void Save(SerializedObject so)
        {
            var data = new Data();

            foreach (var field in Fields)
            {
                var prop = so.FindProperty(field);
                var guid = "";
                if (prop?.objectReferenceValue != null)
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prop.objectReferenceValue));
                SetField(data, field, guid);
            }

            if (IsAllEmpty(data) && File.Exists(BackupPath))
                return;

            if (!Directory.Exists(BackupDir))
                Directory.CreateDirectory(BackupDir);

            File.WriteAllText(BackupPath, JsonUtility.ToJson(data, true));
        }

        public static bool Restore(SerializedObject so)
        {
            if (!File.Exists(BackupPath)) return false;

            Data data;
            try
            {
                data = JsonUtility.FromJson<Data>(File.ReadAllText(BackupPath));
            }
            catch
            {
                return false;
            }

            if (data == null) return false;

            var restored = false;

            foreach (var field in Fields)
            {
                var guid = GetField(data, field);
                if (string.IsNullOrEmpty(guid)) continue;

                var prop = so.FindProperty(field);
                if (prop == null || prop.objectReferenceValue != null) continue;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj == null) continue;

                prop.objectReferenceValue = obj;
                restored = true;
            }

            if (restored)
                so.ApplyModifiedPropertiesWithoutUndo();

            return restored;
        }

        private static bool IsAllEmpty(Data data) =>
            string.IsNullOrEmpty(data.gradients) &&
            string.IsNullOrEmpty(data.defaultFontStack);

        private static void SetField(Data data, string field, string value)
        {
            switch (field)
            {
                case "gradients": data.gradients = value; break;
                case "defaultFontStack": data.defaultFontStack = value; break;
            }
        }

        private static string GetField(Data data, string field) => field switch
        {
            "gradients" => data.gradients,
            "defaultFontStack" => data.defaultFontStack,
            _ => ""
        };
    }
}
