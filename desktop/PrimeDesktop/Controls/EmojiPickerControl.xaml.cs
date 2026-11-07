using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimeDesktop.Controls
{
    public class EmojiItem
    {
        public string Character { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;
    }

    public class EmojiCategoryTab
    {
        public string CategoryId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TabIcon { get; set; } = string.Empty;
        public Brush BackgroundBrush { get; set; } = Brushes.Transparent;
        public Brush BorderBrush { get; set; } = Brushes.Transparent;
    }

    public partial class EmojiPickerControl : UserControl
    {
        public event EventHandler<string>? EmojiSelected;

        private readonly List<EmojiItem> _allEmojis = new();
        private readonly List<EmojiCategoryTab> _categories = new();
        private readonly ObservableCollection<EmojiItem> _displayedEmojis = new();
        private readonly ObservableCollection<EmojiItem> _favoriteEmojis = new();
        private string _selectedCategoryId = "smileys";

        public EmojiPickerControl()
        {
            InitializeComponent();
            InitData();

            EmojisGridControl.ItemsSource = _displayedEmojis;
            FavoritesControl.ItemsSource = _favoriteEmojis;
            CategoryTabsControl.ItemsSource = _categories;

            SelectCategory("smileys");
        }

        private void InitData()
        {
            _categories.Add(new EmojiCategoryTab { CategoryId = "smileys", Title = "Смайлы и эмоции", TabIcon = "😀" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "people", Title = "Люди и жесты", TabIcon = "👍" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "nature", Title = "Животные и природа", TabIcon = "🐱" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "food", Title = "Еда и напитки", TabIcon = "🍕" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "activity", Title = "Активности и спорт", TabIcon = "⚽" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "travel", Title = "Путешествия и транспорт", TabIcon = "🚀" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "objects", Title = "Предметы", TabIcon = "💡" });
            _categories.Add(new EmojiCategoryTab { CategoryId = "symbols", Title = "Символы и знаки", TabIcon = "❤️" });

            // Smileys
            AddEmoji("😀", "Улыбающееся лицо", "smileys", "улыбка радость радостный");
            AddEmoji("😃", "Широкая улыбка", "smileys", "улыбка радость счастье");
            AddEmoji("😄", "Смеющееся лицо с закрытыми глазами", "smileys", "смех глаза хаха");
            AddEmoji("😁", "Сияющее лицо", "smileys", "зубы ухмылка");
            AddEmoji("😆", "Прищуренный смех", "smileys", "смех хохот");
            AddEmoji("😅", "Улыбка с каплей пота", "smileys", "пот волнение облегчение");
            AddEmoji("😂", "Слёзы от смеха", "smileys", "ржу смех слезы lol");
            AddEmoji("🤣", "Катается со смеху", "smileys", "рофл смех лол rofl");
            AddEmoji("😊", "Смущённая улыбка", "smileys", "мило щеки приятно");
            AddEmoji("😇", "Ангел", "smileys", "нимб святой невинный");
            AddEmoji("🙂", "Лёгкая улыбка", "smileys", "ок нормально привет");
            AddEmoji("😉", "Подмигивание", "smileys", "хитро намек подмигнуть");
            AddEmoji("😍", "Влюблённые глаза", "smileys", "любовь сердечки обожаю");
            AddEmoji("🥰", "Влюблённый с сердечками", "smileys", "любовь нежность тепло");
            AddEmoji("😘", "Воздушный поцелуй", "smileys", "чмок поцелуй люблю");
            AddEmoji("😋", "Вкуснятина", "smileys", "язык вкусно ням");
            AddEmoji("😎", "Крутой в очках", "smileys", "очки стиль босс круто");
            AddEmoji("🤩", "В восторге", "smileys", "звезды вау супер");
            AddEmoji("🥳", "Праздник", "smileys", "день рождения вечеринка салют");
            AddEmoji("😏", "Ухмылка", "smileys", "сарказм хитрость флирт");
            AddEmoji("😒", "Недовольный", "smileys", "скука закатил скепсис");
            AddEmoji("😞", "Разочарование", "smileys", "грустно тоска печаль");
            AddEmoji("😔", "Задумчивый грустный", "smileys", "уныние грусть");
            AddEmoji("😟", "Беспокойство", "smileys", "тревога испуг страх");
            AddEmoji("😕", "Смущение", "smileys", "непонятно вопрос");
            AddEmoji("🙁", "Немного грустно", "smileys", "печаль вздох");
            AddEmoji("😢", "Слеза", "smileys", "плач грусть обида");
            AddEmoji("😭", "Громкий плач", "smileys", "рыдания слезы беда");
            AddEmoji("😤", "Торжествующий пар", "smileys", "гордость пар злость");
            AddEmoji("😠", "Злой", "smileys", "гнев раздражение ярость");
            AddEmoji("😡", "Очень злой", "smileys", "ярость бесит огонь");
            AddEmoji("🤬", "Маты и ругань", "smileys", "ругань цензура мат");
            AddEmoji("🤯", "Взрыв мозга", "smileys", "шок офигеть взрыв");
            AddEmoji("😳", "Шок и смущение", "smileys", "неловко стыдно");
            AddEmoji("🥵", "Жарко", "smileys", "пот духота горячо");
            AddEmoji("🥶", "Холодно", "smileys", "мороз лед замерз");
            AddEmoji("😱", "Ужас крик", "smileys", "страх кошмар крик");
            AddEmoji("😨", "Испуг", "smileys", "страшно опасение");
            AddEmoji("😰", "Тревога и пот", "smileys", "стресс паника");
            AddEmoji("🤔", "Думает", "smileys", "мысли размышления вопрос хм");
            AddEmoji("🤫", "Тсс, секрет", "smileys", "тихо молчи секрет");
            AddEmoji("🤭", "Смешок в кулак", "smileys", "хи-хи неловко");
            AddEmoji("🥱", "Зевает", "smileys", "сон скучно спать");
            AddEmoji("😴", "Спит", "smileys", "zzz сон ночь");
            AddEmoji("🤤", "Текут слюнки", "smileys", "еда аппетит слюни");
            AddEmoji("😷", "В маске", "smileys", "болезнь вирус врач");
            AddEmoji("🤒", "С градусником", "smileys", "температура заболел");
            AddEmoji("🤕", "С перевязанной головой", "smileys", "травма боль рана");
            AddEmoji("🤢", "Тошнит", "smileys", "фу отвращение яд");
            AddEmoji("🤮", "Рвота", "smileys", "тошнота бее");
            AddEmoji("🤧", "Чихает", "smileys", "простуда сопли апчхи");
            AddEmoji("🤡", "Клоун", "smileys", "цирк шут дурак рофл");
            AddEmoji("👻", "Привидение", "smileys", "бу хэллоуин призрак");
            AddEmoji("💀", "Череп", "smileys", "смерть умер угар");
            AddEmoji("☠️", "Череп и кости", "smileys", "опасно пират яд");
            AddEmoji("👽", "Пришелец", "smileys", "нло инопланетянин космос");
            AddEmoji("🤖", "Робот", "smileys", "бот ии киборг");
            AddEmoji("💩", "Какашка", "smileys", "какашка смех дерьмо");

            // People & Gestures
            AddEmoji("👍", "Большой палец вверх", "people", "лайк класс ок супер да");
            AddEmoji("👎", "Большой палец вниз", "people", "дизлайк плохо нет отстой");
            AddEmoji("👌", "Окей жестом", "people", "ok хорошо отлично супер");
            AddEmoji("✌️", "Знак мира / победа", "people", "peace победа два");
            AddEmoji("🤞", "Скрещенные пальцы", "people", "удача надежда везет");
            AddEmoji("🤟", "Люблю тебя жест", "people", "рок любовь ily");
            AddEmoji("🤘", "Рокерская коза", "people", "рок метал туса");
            AddEmoji("🤙", "Позвони мне", "people", "звонок шака привет");
            AddEmoji("👈", "Указатель влево", "people", "палец лево туда");
            AddEmoji("👉", "Указатель вправо", "people", "палец право туда");
            AddEmoji("👆", "Указатель вверх", "people", "палец верх внимание");
            AddEmoji("👇", "Указатель вниз", "people", "палец вниз ссылка смотри");
            AddEmoji("☝️", "Палец вверх жест", "people", "минуточку стоп один");
            AddEmoji("✋", "Поднятая ладонь", "people", "стоп привет пять");
            AddEmoji("🖐️", "Раскрытая ладонь", "people", "пять рука стой");
            AddEmoji("👋", "Машет рукой", "people", "привет пока до встречи");
            AddEmoji("👏", "Аплодисменты", "people", "хлопки браво поздравляю");
            AddEmoji("🤝", "Рукопожатие", "people", "сделка договор друзья мир");
            AddEmoji("🙏", "Молитва / благодарность", "people", "спасибо пожалуйста мольба поклон");
            AddEmoji("💪", "Мускулистая рука", "people", "сила спорт качалка мощь");
            AddEmoji("🧠", "Мозг", "people", "ум интеллект мысли");

            // Nature & Animals
            AddEmoji("🐶", "Собака", "nature", "пес щенок друг");
            AddEmoji("🐱", "Кошка", "nature", "кот котик котёнок мяу");
            AddEmoji("🐭", "Мышь", "nature", "мышка грызун");
            AddEmoji("🐹", "Хомяк", "nature", "грызун мило щеки");
            AddEmoji("🐰", "Кролик", "nature", "заяц уши прыг");
            AddEmoji("🦊", "Лиса", "nature", "лисица рыжая хитрый");
            AddEmoji("🐻", "Медведь", "nature", "мишка бурый лес");
            AddEmoji("🐼", "Панда", "nature", "мило бамбук китай");
            AddEmoji("🐨", "Коала", "nature", "эвкалипт австралия");
            AddEmoji("🐯", "Тигр", "nature", "хищник полосатый");
            AddEmoji("🦁", "Лев", "nature", "царь зверей грива");
            AddEmoji("🐸", "Лягушка", "nature", "ква квак жаба пепе");
            AddEmoji("🐵", "Обезьяна", "nature", "мартышка примат джунгли");
            AddEmoji("🦄", "Единорог", "nature", "магия сказка рог");
            AddEmoji("🦋", "Бабочка", "nature", "красота крылья лето");
            AddEmoji("🌺", "Цветок гибискус", "nature", "весна красота природа");
            AddEmoji("🌸", "Сакура", "nature", "вишня япония весна цвет");
            AddEmoji("🌹", "Роза", "nature", "любовь красный цветы");
            AddEmoji("🍀", "Четырёхлистный клевер", "nature", "удача везение клевер");
            AddEmoji("🔥", "Огонь", "nature", "пламя жара пушка топ горячо");
            AddEmoji("⚡", "Молния", "nature", "электричество ток быстро вспышка");
            AddEmoji("✨", "Искры / Блеск", "nature", "звезды магия сияние шик");
            AddEmoji("⭐", "Звезда", "nature", "звездочка ночь оценка");
            AddEmoji("🌟", "Сияющая звезда", "nature", "блеск топ ярко");

            // Food & Drinks
            AddEmoji("🍏", "Зелёное яблоко", "food", "фрукт яблоко витамины");
            AddEmoji("🍎", "Красное яблоко", "food", "фрукт яблоко сочно");
            AddEmoji("🍓", "Клубника", "food", "ягода сладко лето");
            AddEmoji("🍒", "Вишня", "food", "черешня ягода");
            AddEmoji("🥑", "Авокадо", "food", "пп тост здоровая еда");
            AddEmoji("🍕", "Пицца", "food", "еда сыр итальянская");
            AddEmoji("🍔", "Бургер", "food", "фастфуд котлета булка");
            AddEmoji("🍟", "Картошка фри", "food", "фастфуд картофель соус");
            AddEmoji("🍣", "Суши", "food", "япония рыба рис");
            AddEmoji("🍦", "Мороженое", "food", "сладко десерт рожок");
            AddEmoji("🎂", "Торт", "food", "день рождения праздник сладкое");
            AddEmoji("☕", "Кофе / Чай", "food", "утро чашка горячий бодрость");
            AddEmoji("🍺", "Пиво", "food", "бар пивас кружка пятница");
            AddEmoji("🍷", "Вино", "food", "бокал виноград праздник");

            // Activity
            AddEmoji("⚽", "Футбольный мяч", "activity", "футбол игра гол спорт");
            AddEmoji("🏀", "Баскетбольный мяч", "activity", "баскетбол мяч спорт");
            AddEmoji("🎮", "Геймпад / Игры", "activity", "игры приставка гейминг ps xbox");
            AddEmoji("🎯", "Мишень и дротик", "activity", "в цель дартс точно прямо");
            AddEmoji("🎲", "Игральная кость", "activity", "кости рандом настолка удача");
            AddEmoji("🏆", "Кубок чемпиона", "activity", "победа приз чемпион золото");
            AddEmoji("🥇", "Золотая медаль", "activity", "первое место топ награда");

            // Travel & Transport
            AddEmoji("🚗", "Автомобиль", "travel", "машина авто тачка дорога");
            AddEmoji("✈️", "Самолёт", "travel", "полет небо аэропорт отпуск");
            AddEmoji("🚀", "Ракета", "travel", "космос полет взлет старт");
            AddEmoji("🏝️", "Необитаемый остров", "travel", "пляж пальма море отдых");

            // Objects
            AddEmoji("💡", "Лампочка", "objects", "идея свет мысль эврика");
            AddEmoji("📱", "Смартфон", "objects", "телефон айфон мобила экран");
            AddEmoji("💻", "Ноутбук", "objects", "компьютер пк код работа");
            AddEmoji("🎁", "Подарок", "objects", "сюрприз коробка бант праздник");
            AddEmoji("💰", "Мешок денег", "objects", "деньги богатство валюта баксы");
            AddEmoji("🔑", "Ключ", "objects", "доступ замок пароль открой");

            // Symbols
            AddEmoji("❤️", "Красное сердце", "symbols", "любовь сердечко люблю душа");
            AddEmoji("🧡", "Оранжевое сердце", "symbols", "любовь дружба тепло");
            AddEmoji("💛", "Жёлтое сердце", "symbols", "радость тепло солнце");
            AddEmoji("💚", "Зелёное сердце", "symbols", "жизнь природа надежда");
            AddEmoji("💙", "Синее сердце", "symbols", "доверие спокойствие верность");
            AddEmoji("💜", "Фиолетовое сердце", "symbols", "магия стиль мечта");
            AddEmoji("🖤", "Чёрное сердце", "symbols", "стиль тоска готика");
            AddEmoji("🤍", "Белое сердце", "symbols", "чистота мир ангел");
            AddEmoji("💔", "Разбитое сердце", "symbols", "боль расставание грусть");
            AddEmoji("💯", "100 баллов", "symbols", "сотка сто топ идеально база");
            AddEmoji("✅", "Зелёная галочка", "symbols", "готово выполнено верно да");
            AddEmoji("❌", "Красный крест", "symbols", "нет ошибка отмена стоп");
            AddEmoji("⚠️", "Предупреждение", "symbols", "внимание осторожно знак");

            // Populate favorites
            var defaultFavorites = new[] { "😀", "😂", "😍", "👍", "🔥", "❤️", "✨", "🙏", "😎", "🎉", "💯", "🤔", "😭", "🥳" };
            foreach (var fav in defaultFavorites)
            {
                var item = _allEmojis.FirstOrDefault(e => e.Character == fav);
                if (item != null) _favoriteEmojis.Add(item);
            }
        }

        private void AddEmoji(string character, string name, string categoryId, string keywords)
        {
            _allEmojis.Add(new EmojiItem
            {
                Character = character,
                Name = name,
                CategoryId = categoryId,
                Keywords = keywords
            });
        }

        private void SelectCategory(string categoryId)
        {
            _selectedCategoryId = categoryId;
            var cat = _categories.FirstOrDefault(c => c.CategoryId == categoryId);
            if (cat != null && SectionTitleText != null)
            {
                SectionTitleText.Text = cat.Title;
            }

            foreach (var c in _categories)
            {
                bool isSel = c.CategoryId == categoryId;
                c.BackgroundBrush = isSel ? new SolidColorBrush(Color.FromRgb(0x1F, 0x2C, 0x3D)) : Brushes.Transparent;
                c.BorderBrush = isSel ? new SolidColorBrush(Color.FromRgb(0x3B, 0x59, 0x98)) : Brushes.Transparent;
            }

            // Refresh category tabs visually
            CategoryTabsControl.ItemsSource = null;
            CategoryTabsControl.ItemsSource = _categories;

            _displayedEmojis.Clear();
            foreach (var em in _allEmojis.Where(e => e.CategoryId == categoryId))
            {
                _displayedEmojis.Add(em);
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtSearch.Text.Trim().ToLowerInvariant();
            PlaceholderSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            BtnClearSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            if (string.IsNullOrEmpty(query))
            {
                QuickFavoritesPanel.Visibility = Visibility.Visible;
                SelectCategory(_selectedCategoryId);
                return;
            }

            QuickFavoritesPanel.Visibility = Visibility.Collapsed;
            SectionTitleText.Text = "Результаты поиска";

            _displayedEmojis.Clear();
            var matches = _allEmojis.Where(em =>
                em.Name.ToLowerInvariant().Contains(query) ||
                em.Keywords.ToLowerInvariant().Contains(query) ||
                em.Character.Contains(query)
            );

            foreach (var m in matches)
            {
                _displayedEmojis.Add(m);
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = string.Empty;
            TxtSearch.Focus();
        }

        private void CategoryTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string catId)
            {
                if (!string.IsNullOrEmpty(TxtSearch.Text))
                {
                    TxtSearch.Text = string.Empty;
                }
                SelectCategory(catId);
            }
        }

        private void EmojiButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EmojiItem item)
            {
                EmojiSelected?.Invoke(this, item.Character);
            }
        }

        private void EmojiButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EmojiItem item)
            {
                if (FooterEmojiIcon != null) FooterEmojiIcon.Text = item.Character;
                if (FooterEmojiName != null) FooterEmojiName.Text = item.Name;
            }
        }

        private void EmojiButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (FooterEmojiIcon != null) FooterEmojiIcon.Text = "✨";
            if (FooterEmojiName != null) FooterEmojiName.Text = "Prime Emojis • Выберите смайлик";
        }
    }
}
