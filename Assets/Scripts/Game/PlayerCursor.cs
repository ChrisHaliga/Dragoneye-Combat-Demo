using Dragoneye.CameraControl;
using Dragoneye.Multiplayer;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// One player's cursor, replicated to everyone.
    ///
    /// Every client sees every cursor, coloured per player and labelled with that player's name, so
    /// you can tell at a glance where the other side is looking. Only the owner's cursor is driven
    /// by input and followed by the camera.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PlayerCursor : NetworkBehaviour
    {
        [SerializeField, Tooltip("The disc that takes the player's colour.")]
        Renderer m_Marker;

        [SerializeField, Tooltip("Floating name label.")]
        TextMesh m_Label;

        [SerializeField, Tooltip("Local movement. Disabled on cursors we do not own.")]
        CameraCursor m_Cursor;

        [SerializeField, Tooltip("Colour property on the marker material. URP Lit uses _BaseColor.")]
        string m_ColorProperty = "_BaseColor";

        // Written by the owner rather than the server: the name lives on the owner's UGS account,
        // and the server has no way to know it without being told.
        readonly NetworkVariable<FixedString64Bytes> m_PlayerName = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        MaterialPropertyBlock m_PropertyBlock;

        public override void OnNetworkSpawn()
        {
            if (m_Marker == null || m_Label == null || m_Cursor == null)
            {
                Debug.LogError($"{nameof(PlayerCursor)} is missing references on its prefab "
                    + $"(marker={m_Marker != null}, label={m_Label != null}, cursor={m_Cursor != null}).", this);
            }

            m_PropertyBlock = new MaterialPropertyBlock();

            m_PlayerName.OnValueChanged += OnNameChanged;
            ApplyColor();
            ApplyName(m_PlayerName.Value);

            if (IsOwner)
            {
                m_PlayerName.Value = new FixedString64Bytes(Truncate(LocalPlayerName()));
                ArenaCameraBinding.BindLocalCursor(m_Cursor);
            }
            else if (m_Cursor != null)
            {
                // Remote cursors are display only -- their position arrives over the network.
                m_Cursor.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            m_PlayerName.OnValueChanged -= OnNameChanged;
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current) => ApplyColor();

        void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current) =>
            ApplyName(current);

        void ApplyColor()
        {
            var color = PlayerPalette.ForClient(OwnerClientId);

            if (m_Marker != null)
            {
                // A property block rather than material.color, which would leak a material
                // instance per cursor.
                m_Marker.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor(Shader.PropertyToID(m_ColorProperty), color);
                m_Marker.SetPropertyBlock(m_PropertyBlock);
            }

            if (m_Label != null)
            {
                m_Label.color = color;
            }
        }

        void ApplyName(FixedString64Bytes value)
        {
            if (m_Label == null)
            {
                return;
            }

            var name = value.ToString();
            m_Label.text = string.IsNullOrEmpty(name) ? $"Player {OwnerClientId}" : name;
        }

        string LocalPlayerName()
        {
            // SessionRunner survives the scene load, so the lobby name is still available here.
            var runner = SessionRunner.Instance;
            var name = runner != null ? runner.PlayerName : null;
            return string.IsNullOrEmpty(name) ? $"Player {OwnerClientId}" : name;
        }

        // FixedString64Bytes holds 61 bytes of UTF-8. Assigning something longer throws, and a
        // display name is not worth an exception.
        static string Truncate(string value) =>
            value.Length <= 30 ? value : value.Substring(0, 30);
    }
}
