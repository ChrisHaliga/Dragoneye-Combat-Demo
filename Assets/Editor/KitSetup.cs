using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Writes the two new pieces of kit onto disk: the greataxe taking both hands, and the offhand
    /// dagger that makes its holder quicker.
    ///
    /// Both are authored by <see cref="CharacterContentSetup"/>, which a fresh clone runs anyway.
    /// This exists so an existing project can have them without running every other step, and it
    /// is spent the moment it has: run it once, then delete this file.
    /// </summary>
    static class KitSetup
    {
        [MenuItem("ClaudeCode/Author The Greataxe And The Offhand Dagger")]
        static void Run()
        {
            CharacterContentSetup.Run();
            AssetDatabase.SaveAssets();
            Debug.Log("Kit authored. Delete Assets/Editor/KitSetup.cs now.");
        }
    }
}
