// Этот файл создаёт «VeriKeygen staff». Здесь только соль и PBKDF2-хэш паролей, самих паролей нет.
namespace VeriPlayer.Services
{
    public static class BuiltInStaff
    {
        public sealed class Entry
        {
            public string Nick;
            public Models.UserRole Role;
            public string Salt;
            public string Hash;
            public int Iterations;
            public string Bio;
        }

        public static readonly Entry[] All =
        {
            new Entry
            {
                Nick = "Verilunim",
                Role = Models.UserRole.Owner,
                Salt = "XOI6VpvwLW4Gy1d/twITeQ==",
                Hash = "bs6SWZHzlzagTeSG0bnA3Fxr5vMCTfcugnLXuXNJog0=",
                Iterations = 600000,
                Bio = "Основатель Veri Player. Следит за порядком и развитием проекта."
            },
            new Entry
            {
                Nick = "Jomapel",
                Role = Models.UserRole.CoOwner,
                Salt = "kpJ1AfyVkv/K0VCUY9vJPg==",
                Hash = "X9TuK6jcB0Au1ObB3rFfwy+4S+cA2yziY5KTERZxUGU=",
                Iterations = 600000,
                Bio = "Со-владелец Veri Player. Помогает владельцу и следит за пользователями."
            }
        };
    }
}
