using System.IO;
using Dragoneye.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-shot wiring for the session UI. Everything here can be done by hand in the
    /// inspector; this just does it correctly and idempotently.
    /// </summary>
    static class MultiplayerSceneSetup
    {
        const string k_ThemePath = "Assets/UI Toolkit/UnityDefaultRuntimeTheme.tss";
        const string k_PanelSettingsPath = "Assets/UI/SessionPanelSettings.asset";
        const string k_UxmlPath = "Assets/UI/SessionMenu.uxml";
        const string k_QuickstartUIName = "Temporary UI -- can be deleted";

        [MenuItem("Dragoneye/Multiplayer/Set Up Session UI In Open Scene")]
        static void SetUp()
        {
            var scene = SceneManager.GetActiveScene();

            var networkManager = Object.FindAnyObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                EditorUtility.DisplayDialog("Session UI",
                    "No NetworkManager in the open scene. Open the multiplayer scene first.", "OK");
                return;
            }

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_UxmlPath);
            if (visualTree == null)
            {
                EditorUtility.DisplayDialog("Session UI", $"Could not find {k_UxmlPath}.", "OK");
                return;
            }

            var panelSettings = EnsurePanelSettings();

            // Relay overwrites the transport's connection data at runtime, but leaving the
            // quickstart's loopback listen address in place is misleading. 0.0.0.0 is also
            // what a direct-connect fallback would need.
            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport != null && transport.ConnectionData.ServerListenAddress == "127.0.0.1")
            {
                Undo.RecordObject(transport, "Set listen address");
                transport.ConnectionData.ServerListenAddress = "0.0.0.0";
                EditorUtility.SetDirty(transport);
            }

            if (Object.FindAnyObjectByType<SessionRunner>() == null)
            {
                var runner = new GameObject("Session Runner", typeof(SessionRunner));
                Undo.RegisterCreatedObjectUndo(runner, "Create Session Runner");
            }

            if (Object.FindAnyObjectByType<SessionMenuUI>() == null)
            {
                var uiObject = new GameObject("Session UI", typeof(UIDocument), typeof(SessionMenuUI));
                var document = uiObject.GetComponent<UIDocument>();
                document.panelSettings = panelSettings;
                document.visualTreeAsset = visualTree;
                Undo.RegisterCreatedObjectUndo(uiObject, "Create Session UI");
            }

            // The quickstart's uGUI buttons call StartHost/StartClient directly, which would
            // fight the session for control of the NetworkManager.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == k_QuickstartUIName)
                {
                    Undo.DestroyObjectImmediate(root);
                    break;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Session UI is set up. Save the scene and press Play.");
        }

        static PanelSettings EnsurePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(k_PanelSettingsPath);
            if (existing != null)
            {
                return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(k_PanelSettingsPath));

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(k_ThemePath);
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);

            AssetDatabase.CreateAsset(panelSettings, k_PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return panelSettings;
        }
    }
}
