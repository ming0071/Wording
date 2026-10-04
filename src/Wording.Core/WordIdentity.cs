using System.Security.Cryptography;
using System.Text;

namespace Wording.Core;

public static class WordIdentity
{
    public static Guid For(string headword)
    {
        var normalized = string.Join(" ", headword.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("wording-headword-v1\n" + normalized)).AsSpan(0, 16));
    }
}
