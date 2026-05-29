using System.Text;

namespace Edict.Generators;

/// <summary>
/// In-house port of <c>System.Text.Json.JsonNamingPolicy.SnakeCaseLower</c>,
/// which isn't reachable from netstandard2.0 generators. Behaviour is pinned
/// to the BCL output for the worked-examples table by
/// <c>TelemeterizedTagNamingTests</c>.
/// </summary>
internal static class SnakeCaseLower
{
    public static string Convert(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = name[i - 1];
                var prevIsLower = char.IsLower(prev);
                var prevIsUpper = char.IsUpper(prev);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (prevIsLower || (prevIsUpper && nextIsLower))
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
