using Ashburn.Player;
using Ashburn.Radio;
using Photon.Voice.PUN;
using Photon.Voice.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Wires the handset up, because doing it by hand is nine components in two files and the
    /// failure when one is missed says nothing.
    ///
    /// Multiplayer.MD §8 designed the radio and left the wiring undone. Every script it names
    /// already exists; none of them were on anything, so pressing the key opened a channel that
    /// nothing was listening to. This puts them where that section says they go.
    ///
    /// Safe to run again. Every step asks whether it has already been done, so a second run on a
    /// half-wired prefab finishes the job rather than doubling it.
    /// </summary>
    static class RadioSetup
    {
        const string CharacterPath = "Assets/Resources/Nathan.prefab";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string SystemsScene = "Assets/Scenes/Systems.unity";

        [MenuItem("Ashburn/Wire Up The Radio")]
        static void Wire()
        {
            var character = WireCharacter();
            var systems = WireSystems();

            EditorUtility.DisplayDialog(
                "Radio",
                $"{character}\n\n{systems}\n\n" +
                "Grant inherits all of it: it is a variant of Nathan.",
                "Right");
        }

        /// <summary>
        /// Puts the handset, the microphone and the speaker on the character.
        ///
        /// All on the one object on purpose. PhotonVoiceView looks for a Recorder and a Speaker on
        /// itself and its children, and uses the first for the character you own and the second for
        /// the one you do not — so a single prefab covers both ends and there is no second prefab
        /// to keep in agreement with this one.
        /// </summary>
        static string WireCharacter()
        {
            var root = PrefabUtility.LoadPrefabContents(CharacterPath);
            if (root == null)
                return $"Could not open {CharacterPath}.";

            var added = 0;

            // The speaker's own AudioSource. Flat, not placed in the world: Multiplayer.MD §8 is
            // firm that the radio is a sound inside the listener's headset, not one coming from
            // wherever their partner happens to be standing.
            var audio = Ensure<AudioSource>(root, ref added);
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.loop = true;

            var recorder = Ensure<Recorder>(root, ref added);

            // Off until the key goes down. The microphone still runs, so the channel opens with no
            // delay — see PhotonRadioBridge — but nothing leaves the machine in the meantime.
            recorder.TransmitEnabled = false;
            recorder.VoiceDetection = false;

            Ensure<Speaker>(root, ref added);
            Ensure<PhotonVoiceView>(root, ref added);

            var transmitter = Ensure<RadioTransmitter>(root, ref added);
            AssignIfEmpty(transmitter, "inputActions",
                          AssetDatabase.LoadAssetAtPath<Object>(ActionsPath));

            var bridge = Ensure<PhotonRadioBridge>(root, ref added);
            AssignIfEmpty(bridge, "recorder", recorder);

            // The headset's lie, made on the listening machine. It filters this object's
            // AudioSource, which is the Speaker's, so it only ever colours a partner's voice —
            // the character you own has nothing coming out of its speaker to filter.
            var dsp = Ensure<RadioDsp>(root, ref added);

            // Finds its own partner and listener at runtime, so there is nothing to assign.
            Ensure<HeadsetLink>(root, ref added);

            var archive = Ensure<VoiceArchive>(root, ref added);
            AssignIfEmpty(archive, "output", dsp);

            var listed = ListTransmitterAsInput(root, transmitter);

            PrefabUtility.SaveAsPrefabAsset(root, CharacterPath);
            PrefabUtility.UnloadPrefabContents(root);

            return $"Nathan: {added} component(s) added" +
                   (listed ? ", handset listed as input." : ".");
        }

        /// <summary>
        /// Adds the transmitter to <see cref="PlayerRig"/>'s input list.
        ///
        /// Left out, a partner's copy keeps its transmitter enabled and opens their microphone from
        /// this player's keyboard. §8 calls this out by name as the one that is easy to miss.
        /// </summary>
        static bool ListTransmitterAsInput(GameObject root, RadioTransmitter transmitter)
        {
            var rig = root.GetComponent<PlayerRig>();
            if (rig == null)
                return false;

            var serialized = new SerializedObject(rig);
            var list = serialized.FindProperty("inputComponents");
            if (list == null)
                return false;

            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == transmitter)
                    return false;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = transmitter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// Makes sure the voice client is in the systems scene.
        ///
        /// It follows the PUN room on its own — join a room and the voice room follows — so it
        /// needs nothing but to exist, and it belongs beside the rest of the systems rather than in
        /// a map, which is loaded and dropped underneath it.
        /// </summary>
        static string WireSystems()
        {
            // Opened only if it is not already. Closing a scene somebody has open would take their
            // unsaved work with it, which is a steep price for a menu item they ran to save time.
            var scene = SceneManager.GetSceneByPath(SystemsScene);
            var wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen)
                scene = EditorSceneManager.OpenScene(SystemsScene, OpenSceneMode.Additive);

            var result = "Systems: voice client already there.";

            if (Object.FindAnyObjectByType<PunVoiceClient>() == null)
            {
                var host = new GameObject("PunVoiceClient");
                host.AddComponent<PunVoiceClient>();
                EditorSceneManager.MoveGameObjectToScene(host, scene);
                EditorSceneManager.MarkSceneDirty(scene);

                // Left dirty for whoever has it open to save with the rest of their work. Saving
                // out from under them would commit whatever else they had changed.
                if (!wasOpen)
                    EditorSceneManager.SaveScene(scene);

                result = wasOpen
                    ? "Systems: voice client added — save the scene."
                    : "Systems: voice client added.";
            }

            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);

            return result;
        }

        static T Ensure<T>(GameObject go, ref int added) where T : Component
        {
            var existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            added++;
            return go.AddComponent<T>();
        }

        /// <summary>Fills a serialized reference only when nobody has set it already.</summary>
        static void AssignIfEmpty(Object owner, string field, Object value)
        {
            if (owner == null || value == null)
                return;

            var serialized = new SerializedObject(owner);
            var property = serialized.FindProperty(field);
            if (property == null || property.objectReferenceValue != null)
                return;

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
