using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The ring under a unit, answering two questions at once: which side it fights for, and which
    /// player runs it.
    ///
    /// Party is the outer ring, because "friend or enemy" is the question a player asks constantly.
    /// The controlling player is a thin inner accent, hidden entirely for computer-controlled
    /// creatures -- an absent accent reads as "not anyone's" more clearly than a grey one would.
    ///
    /// Hover and selection are brightness and scale states of the same ring rather than new visuals,
    /// so the board never gains a second thing to read.
    ///
    /// Colour alone will not carry four parties for colourblind players. A party sigil on the ring
    /// is the fix; it is not in this slice.
    /// </summary>
    [RequireComponent(typeof(CreatureState))]
    [DisallowMultipleComponent]
    public sealed class UnitOwnershipRing : MonoBehaviour
    {
        [SerializeField, Tooltip("Outer ring. Takes the party colour.")]
        Renderer m_PartyRing;

        [SerializeField, Tooltip("Inner accent. Takes the controlling player's colour; hidden for AI.")]
        Renderer m_PlayerAccent;

        [SerializeField, Tooltip("Colour property on the ring material. URP Lit uses _BaseColor.")]
        string m_ColorProperty = "_BaseColor";

        [SerializeField, Range(1f, 2f), Tooltip("Scale multiplier while hovered or selected.")]
        float m_EmphasisScale = 1.15f;

        [SerializeField, Range(1f, 4f), Tooltip("Brightness multiplier while hovered or selected.")]
        float m_EmphasisBrightness = 1.6f;

        CreatureState m_State;
        MaterialPropertyBlock m_PropertyBlock;
        int m_ColorPropertyId;
        Vector3 m_BaseScale;
        bool m_Emphasised;

        void Awake()
        {
            m_State = GetComponent<CreatureState>();
            m_PropertyBlock = new MaterialPropertyBlock();
            m_ColorPropertyId = Shader.PropertyToID(m_ColorProperty);

            if (m_PartyRing == null)
            {
                Debug.LogError($"{nameof(UnitOwnershipRing)} has no party ring assigned.", this);
                enabled = false;
                return;
            }

            m_BaseScale = m_PartyRing.transform.localScale;
        }

        void OnEnable()
        {
            m_State.Changed += Repaint;
            Repaint();
        }

        void OnDisable() => m_State.Changed -= Repaint;

        /// <summary>Hover or selection emphasis. Same ring, brighter and slightly larger.</summary>
        public void SetEmphasised(bool emphasised)
        {
            if (m_Emphasised == emphasised)
            {
                return;
            }

            m_Emphasised = emphasised;
            m_PartyRing.transform.localScale = emphasised ? m_BaseScale * m_EmphasisScale : m_BaseScale;
            Repaint();
        }

        void Repaint()
        {
            var partyColor = PartyPalette.ForParty(m_State.Party);
            if (m_Emphasised)
            {
                partyColor *= m_EmphasisBrightness;
                partyColor.a = 1f;
            }

            Paint(m_PartyRing, partyColor);

            if (m_PlayerAccent == null)
            {
                return;
            }

            // Absent rather than grey: nothing to read is a clearer "nobody" than a muted colour
            // sitting alongside four real ones.
            var controlled = !m_State.IsComputerControlled;
            m_PlayerAccent.gameObject.SetActive(controlled);

            if (controlled)
            {
                Paint(m_PlayerAccent, PlayerPalette.ForSlot(m_State.ControllerSlot));
            }
        }

        void Paint(Renderer renderer, Color color)
        {
            // A property block rather than material.color, which would leak an instance per unit and
            // opt the ring out of instancing.
            renderer.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(m_ColorPropertyId, color);
            renderer.SetPropertyBlock(m_PropertyBlock);
        }
    }
}
