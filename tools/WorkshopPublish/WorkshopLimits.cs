using System.Text;
using Steamworks;

internal static class WorkshopLimits
{
    // Leave room for the native strings' terminating NUL.
    public const int TitleMaxBytes = Constants.k_cchPublishedDocumentTitleMax - 1;
    public const int DescriptionMaxBytes = Constants.k_cchPublishedDocumentDescriptionMax - 1;
    public const int ChangeNoteMaxBytes = Constants.k_cchPublishedDocumentChangeDescriptionMax - 1;

    // Current image URLs are about 102 bytes. Budget 256 per occurrence before
    // contacting Steam, then validate the actual rendered description as well.
    public const int EstimatedImageUrlBytes = 256;

    public static int Validate(string field, string value, int maxBytes)
    {
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > maxBytes)
            throw new InvalidDataException($"{field} is {bytes:N0} UTF-8 bytes; the publishing limit is {maxBytes:N0}. Shorten it before publishing.");
        return bytes;
    }
}
