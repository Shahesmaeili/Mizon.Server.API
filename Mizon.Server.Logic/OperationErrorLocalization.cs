using System.Globalization;

namespace Mizon.Server.Logic;

public static class OperationErrorLocalization
{
    internal const string RegistrationRequired = "Registration details are required.";
    internal const string RegistrationName = "First name and last name must contain 1 to 60 characters.";
    internal const string RegistrationUsername = "Username must contain 3 to 32 letters, numbers, dots or underscores.";
    internal const string RegistrationEmail = "A valid email address of at most 128 characters is required.";
    internal const string RegistrationPassword = "Password must contain 8 to 128 characters.";
    internal const string RegistrationInvalidCharacters = "Username or email address contains invalid characters.";
    internal const string DeviceInformationRequired = "Valid platform and device information is required.";

    /// <summary>Localizes API-owned error text. Machine-readable codes and response data are preserved.</summary>
    public static void Apply(BaseApiError error)
    {
        var persian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        var message = error.Code switch
        {
            100 => ("Client not found", "کاربر پیدا نشد", null, null),
            101 => ("Session not found", "نشست کاربر پیدا نشد", null, null),
            400 => ("ClientPublicId is required", "شناسهٔ عمومی کاربر الزامی است", null, null),
            401 => ("Unauthorized", "دسترسی غیرمجاز", null, null),
            404 => ("Client not found", "کاربر پیدا نشد", null, null),
            1000 => ("Not valid request.", "درخواست نامعتبر است",
                "The PlatformVersion not valid.", error.Details == DeviceInformationRequired
                    ? "اطلاعات معتبر پلتفرم و دستگاه را وارد کنید." : "نسخهٔ پلتفرم معتبر نیست."),
            1001 => ("Login Failed", "ورود ناموفق بود",
                "Email or password is incorrect.", "ایمیل، نام کاربری یا رمز عبور اشتباه است."),
            2001 => ("Session not found", "نشست کاربر پیدا نشد",
                "The session does not exist.", "این نشست وجود ندارد."),
            OperationErrorCodes.Clients.ClientSessionNotFound => ("Session not found", "نشست کاربر پیدا نشد", null, null),
            OperationErrorCodes.Clients.InvalidRegistration => ("Invalid registration details", "اطلاعات ثبت‌نام معتبر نیست",
                null, RegistrationDetails(error.Details)),
            OperationErrorCodes.Clients.UsernameUnavailable => ("Registration failed", "ثبت‌نام ناموفق بود",
                "This username is already in use.", "این نام کاربری قبلاً استفاده شده است."),
            OperationErrorCodes.Clients.EmailUnavailable => ("Registration failed", "ثبت‌نام ناموفق بود",
                "This email address is already in use.", "این ایمیل قبلاً استفاده شده است."),
            OperationErrorCodes.Clients.RegistrationTemporarilyUnavailable => ("Registration temporarily unavailable", "ثبت‌نام موقتاً در دسترس نیست",
                "Please try again shortly.", "کمی بعد دوباره تلاش کنید."),
            OperationErrorCodes.Clients.InvalidFollowingActivitiesRequest => ("Invalid activity request", "درخواست فعالیت‌ها نامعتبر است", null, null),
            OperationErrorCodes.Clients.InvalidClientMood => ("Invalid mood", "حالت انتخاب‌شده معتبر نیست", null, null),
            OperationErrorCodes.Musics.MusicNotFound => ("Track not found", "آهنگ پیدا نشد", null, null),
            OperationErrorCodes.Musics.MusicNotActive => ("Track unavailable", "آهنگ در دسترس نیست", null, null),
            OperationErrorCodes.Musics.MusicFileNotFound => ("Audio file not found", "فایل صوتی پیدا نشد", null, null),
            OperationErrorCodes.Musics.MusicLyricNotFound => ("Lyrics not found", "متن آهنگ پیدا نشد", null, null),
            OperationErrorCodes.Musics.MusicReactionTypeNotFound => ("Reaction type not found", "نوع واکنش پیدا نشد", null, null),
            OperationErrorCodes.Musics.MusicPlaylistNotFound => ("Playlist not found", "پلی‌لیست پیدا نشد", null, null),
            OperationErrorCodes.Musics.InvalidMusicPlaylistTitle => ("Invalid playlist title", "عنوان پلی‌لیست معتبر نیست", null, null),
            OperationErrorCodes.Musics.InvalidMusicPlaylistCursor => ("Invalid pagination cursor", "نشانگر صفحه‌بندی معتبر نیست", null, null),
            OperationErrorCodes.Musics.InvalidMusicPlayRequest => ("Invalid playback request", "درخواست پخش معتبر نیست", null, null),
            11000 => ("Story not found", "استوری پیدا نشد", null, null),
            11001 => ("Invalid story media or media type", "رسانه یا نوع رسانهٔ استوری معتبر نیست", null, null),
            11002 => ("Invalid story content", "محتوای استوری معتبر نیست", null, null),
            11003 => ("Invalid story widget", "ابزارک استوری معتبر نیست", null, null),
            OperationErrorCodes.Medias.MediaNotFound => ("Media not found", "رسانه پیدا نشد", null, null),
            _ => (null, null, null, null)
        };

        var (englishTitle, persianTitle, englishDetails, persianDetails) = message;
        if (persian && persianTitle is not null)
        {
            error.Title = persianTitle;
            error.Details = persianDetails ?? string.Empty;
        }
        else
        {
            if (string.IsNullOrEmpty(error.Title)) error.Title = englishTitle ?? "Request failed";
            if (string.IsNullOrEmpty(error.Details)) error.Details = englishDetails ?? string.Empty;
        }
    }

    private static string RegistrationDetails(string? details) => details switch
    {
        RegistrationRequired => "اطلاعات ثبت‌نام را وارد کنید.",
        RegistrationName => "نام و نام خانوادگی هر کدام باید بین ۱ تا ۶۰ نویسه داشته باشند.",
        RegistrationUsername => "نام کاربری باید ۳ تا ۳۲ نویسه و شامل حروف انگلیسی، اعداد، نقطه یا زیرخط باشد.",
        RegistrationEmail => "یک ایمیل معتبر با حداکثر ۱۲۸ نویسه وارد کنید.",
        RegistrationPassword => "رمز عبور باید بین ۸ تا ۱۲۸ نویسه داشته باشد.",
        RegistrationInvalidCharacters => "نام کاربری یا ایمیل نویسهٔ نامعتبر دارد.",
        _ => "اطلاعات ثبت‌نام را بررسی کنید."
    };
}
