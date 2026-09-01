using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Draws a focus point: a disc in the owner's colour and a name label above it.
    ///
    /// Pure view. It observes <see cref="FocusState"/> and <see cref="PlayerRoster"/> and writes
    /// nothing back, so the replicated state has no idea a marker or a label exists.
    /// </summary>
    [RequireComponent(typeof(FocusState))]
    [DisallowMultipleComponent]
    public sealed class FocusView : MonoBehaviour
    {
        [SerializeField, Tooltip("The disc that takes the player's colour.")]
        Renderer m_Marker;

        [SerializeField, Tooltip("Floating name label.")]
        TextMesh m_Label;

        [SerializeField, Tooltip("Colour property on the marker material. URP Lit uses _BaseColor.")]
        string m_ColorProperty = "_BaseColor";

        FocusState m_State;
        MaterialPropertyBlock m_PropertyBlock;
        int m_ColorPropertyId;

        void Awake()
        {
            m_State = GetComponent<FocusState>();
            m_PropertyBlock = new MaterialPropertyBlock();

            // Cached rather than resolved per repaint: the id never changes, and the lookup is a
            // string hash. Matches HexMapRenderer and CameraRig.
            m_ColorPropertyId = Shader.PropertyToID(m_ColorProperty);

            if (m_Marker == null || m_Label == null)
            {
                Debug.LogError($"{nameof(FocusView)} is missing its marker or label reference.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            m_State.SlotChanged += Repaint;

            if (PlayerRoster.Current != null)
            {
                PlayerRoster.Current.Changed += Repaint;
            }

            Repaint();
        }

        void OnDisable()
        {
            m_State.SlotChanged -= Repaint;

            if (PlayerRoster.Current != null)
            {
                PlayerRoster.Current.Changed -= Repaint;
            }
        }

        void Repaint()
        {
            var slot = m_State.Slot;
            var color = PlayerPalette.ForSlot(slot);

            // A property block rather than material.color, which would leak a material instance
            // per focus point.
            m_Marker.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(m_ColorPropertyId, color);
            m_Marker.SetPropertyBlock(m_PropertyBlock);

            m_Label.color = color;
            m_Label.text = ResolveName(slot);
        }

        string ResolveName(int slot)
        {
            var owner = m_State.OwnerClientId;

            if (PlayerRoster.Current != null && PlayerRoster.Current.TryGet(owner, out var entry))
            {
                var name = entry.Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return slot >= 0 ? $"Player {slot + 1}" : "Player";
        }
    }
}
