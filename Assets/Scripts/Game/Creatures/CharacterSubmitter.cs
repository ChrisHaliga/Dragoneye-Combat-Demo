using Dragoneye.Data;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Offers this player's chosen character to the host, once there is a host to offer it to.
    ///
    /// Polls rather than hooking a callback: the match object is spawned some frames after the
    /// lobby appears, the roster issues this player a slot some frames after that, and a submission
    /// before either exists is silently lost. Watching for the moment all three are ready is simpler
    /// than ordering three events that arrive over a network.
    ///
    /// Re-submits when the selection changes, so editing a character and returning to the lobby
    /// sends the edited one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSubmitter : MonoBehaviour
    {
        SavedCharacter m_Submitted;
        PlayerCharacters m_SubmittedTo;

        void Update()
        {
            var characters = PlayerCharacters.Current;
            var chosen = SelectedCharacter.Current;

            if (characters == null || chosen == null)
            {
                // A new match gets a new PlayerCharacters, so forgetting here is what makes the
                // next one receive a submission at all.
                m_SubmittedTo = null;
                m_Submitted = null;
                return;
            }

            if (ReferenceEquals(chosen, m_Submitted) && characters == m_SubmittedTo)
            {
                return;
            }

            if (!LocalPlayer.TryGetSlot(out _))
            {
                // No slot yet. Try again next frame rather than submitting as nobody.
                return;
            }

            characters.Submit(chosen.Build);

            m_Submitted = chosen;
            m_SubmittedTo = characters;
        }
    }
}
