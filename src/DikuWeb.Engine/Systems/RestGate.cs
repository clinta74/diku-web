using DikuWeb.Domain.Characters;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// Why a character sitting or lying down cannot do something, or null when they are on their feet.
/// </summary>
/// <remarks>
/// <para>
/// <b>One gate, because three verbs disagreed.</b> Movement refused a resting character and
/// <c>attack</c> and <c>cast</c> did not, so the only thing rest actually stopped was walking —
/// you could open a fight from your bedroll, hold it to the end, and never stand up. The regen
/// tables make that worth doing: resting and sleeping regenerate faster than standing, and nothing
/// was charging for it.
/// </para>
/// <para>
/// <b>It refuses rather than standing you up.</b> Auto-standing would be friendlier for one
/// keystroke and worse afterwards: rest is a decision with a cost, and a verb that silently
/// cancels it is a verb that loses the player their regen without saying so. Being told is the
/// point — the message names the state you are in, since "you must stand up first" answers a
/// question nobody asked when they thought they were already standing.
/// </para>
/// <para>
/// Combat is a separate gate (<c>Travel.Refuse</c>, <c>HostileActionGate</c>) and stays that way.
/// This one is only about posture.
/// </para>
/// </remarks>
public static class RestGate
{
    /// <summary>Why <paramref name="character"/> cannot act, or null if they can.</summary>
    /// <remarks>
    /// Both refusals name <c>stand</c>, because that is the verb — and the first draft of this did
    /// not. "You will have to wake up to attack" is true, sounds helpful, and leaves a player
    /// hunting for a <c>wake</c> command that does not exist. It follows the shape <c>flee</c>
    /// already uses on the combat gate: say the state, then name the way out of it.
    /// </remarks>
    public static string? Refuse(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        return character.RestState switch
        {
            CharacterRestState.Sleep => "You are asleep. Try 'stand' first.",
            CharacterRestState.Rest => "You are sitting down. Try 'stand' first.",
            _ => null,
        };
    }
}
