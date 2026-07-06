using System;
using UnityEngine;

/// <summary>
/// Letter data only.
/// Visibility/permission should be handled by other components (e.g., StickyObject + SharedState).
/// </summary>
[DisallowMultipleComponent]
public class LetterObject : MonoBehaviour
{
    public string letter = "A";      // Expected: single char like "C" (case-insensitive)

    public char GetLetterChar()
    {
        if (string.IsNullOrWhiteSpace(letter)) return '\0';
        return char.ToUpperInvariant(letter.Trim()[0]);
    }
}

