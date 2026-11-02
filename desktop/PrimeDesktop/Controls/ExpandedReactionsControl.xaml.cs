using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrimeDesktop.Controls
{
    public class ReactionItem
    {
        public string Character { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "all";
        public string Keywords { get; set; } = string.Empty;
    }

    public partial class ExpandedReactionsControl : UserControl
    {
        public event EventHandler<string>? ReactionSelected;
        public event EventHandler? OpenFullEmojiPickerRequested;
        public event EventHandler? CloseRequested;

        private readonly List<ReactionItem> _allReactions = new();
        private readonly ObservableCollection<ReactionItem> _displayedReactions = new();
        private string _currentCategory = "all";

        public ExpandedReactionsControl()
        {
            InitializeComponent();
            InitReactions();
            ReactionsItemsControl.ItemsSource = _displayedReactions;
        }

        private void InitReactions()
        {
            var list = new (string Character, string Name, string Category, string Keywords)[]
            {
                // Top reactions (matches mobile client priority)
                ("❤️", "Красное сердце", "top", "сердце любовь love heart топ обожаю лайк"),
                ("🔥", "Огонь", "top", "огонь пламя fire hot пушка топ разнос жара круто"),
                ("👍", "Большой палец вверх", "top", "лайк like класс супер да отлично хорошо plus"),
                ("👎", "Большой палец вниз", "top", "дизлайк dislike против нет фу плохой минус"),
                ("😂", "Слёзы от смеха", "top", "смех ржач lol rofl хаха ржу ору смешно"),
                ("🥰", "Влюблённое лицо", "top", "любовь сердечки мило умиление обожаю cute love"),
                ("😮", "Удивление", "top", "шок вау офигеть о боже wow omo ооо"),
                ("😢", "Грустное лицо", "top", "плач слезы грусть печаль обида жалко sad cry"),
                ("🙏", "Молитва / Спасибо", "top", "спасибо благодарю пожалуйста pray мольба поклон thanks"),
                ("👏", "Аплодисменты", "top", "браво хлопки clap аплодисменты молодец ура applause"),
                ("🎉", "Праздник", "top", "хлопушка салют пати вечеринка праздник поздравляю ура party"),
                ("💯", "Сто процентов", "top", "сто 100 топ точно база факт сотня 100%"),
                ("⚡", "Молния", "top", "молния ток энергия скорость flash thunder fast"),
                ("🌟", "Сияющая звезда", "top", "звезда блеск star свет топ огонь"),
                ("🤩", "Звёздные глаза", "top", "восторг вау звезды супер круто потрясающе wow"),
                ("🥳", "Праздничный колпак", "top", "праздник др день рождения туса праздник party"),

                // Faces & Emotions
                ("😎", "В тёмных очках", "faces", "крутой босс круто очки cool swagger стиль"),
                ("🤔", "Задумчивый", "faces", "думаю мысли размышления вопрос хм think hmm"),
                ("😏", "Ухмылка", "faces", "ухмылка хитрец smirk ухмыляюсь"),
                ("🥺", "Умоляющие глаза", "faces", "пожалуйста мило щеночек слёзки умоляю cute please"),
                ("🤯", "Взрыв мозга", "faces", "шок взрыв мозга офигеть mind blown boom"),
                ("😱", "Крик ужаса", "faces", "ужас страх крик scream omg жесть испуг"),
                ("😡", "Сердитое лицо", "faces", "злость гнев ярость бесит злой angry"),
                ("🤬", "Цензура и ругань", "faces", "мат ругань злость ярость rage swearing"),
                ("🤡", "Клоун", "faces", "клоун цирк clown шут рофл кринж мем"),
                ("💩", "Какашка", "faces", "какашка говно poop shit прикол шутка рофл"),
                ("😴", "Спящий", "faces", "сон спит zzz спокойной ночи спать устал sleep"),
                ("🤮", "Тошнота", "faces", "рвота фу блевота тошнит sick vomit мерзко"),
                ("🫡", "Воинское приветствие", "faces", "салют так точно принял понял служу уважение respect salute"),
                ("🤫", "Тссс", "faces", "тихо молчи секрет тсс шшш shh secret"),
                ("🙄", "Закатывает глаза", "faces", "закатил глаза душно кринж закатывание roll eyes"),
                ("😜", "Лицо с языком", "faces", "язык подмигивание шутка рофл подкол прикол"),
                ("🤪", "Безумное лицо", "faces", "дурачусь безумие дикий язык crazy goofy"),
                ("😇", "Ангел с нимбом", "faces", "святой ангел невиновный нимб angel innocent"),
                ("🤤", "Слюнки текут", "faces", "вкусно слюни еда аппетит drooling yummy"),
                ("🥶", "Замёрзший", "faces", "холод мороз зима лед замерз cold freezing"),
                ("🥵", "Жара", "faces", "жарко пот душно лето hot sweating"),
                ("🧐", "С моноклем", "faces", "хм изучаю монокль детектив расследование monocle inspect"),
                ("💀", "Череп", "faces", "умер мертв череп dead skull смерть ор разрыв смеха"),
                ("👻", "Привидение", "faces", "призрак бу ghost дух мистика привидение"),
                ("🤖", "Робот", "faces", "бот робот robot ии ai код нейросеть"),
                ("😭", "Громкий плач", "faces", "рыдания плачу в голос слёзы cry sobbing"),
                ("🤣", "Катается со смеху", "faces", "ржу не могу угар rofl смех ор разрыв"),
                ("🤗", "Обнимашки", "faces", "объятия тепло обнимаю hug warmth"),
                ("🫢", "Рука у рта", "faces", "ой шок секрет прикрыл рот surprise oops"),
                ("🥱", "Зевок", "faces", "зевает скука скучно спать хочу устал yawn"),
                ("🤠", "Ковбой", "faces", "ковбой шляпа вестерн прикол юху cowboy"),
                ("😈", "Улыбающийся черт", "faces", "черт бес чертенок шалость devil evil хитрец"),

                // Hands & Gestures
                ("🤝", "Рукопожатие", "hands", "сделка договорились рукопожатие hand shake мир привет"),
                ("👋", "Машет рукой", "hands", "привет пока волна hand wave hi bye"),
                ("✌️", "Знак мира", "hands", "мир победа два пальца victory peace бро"),
                ("🤞", "Скрещенные пальцы", "hands", "на удачу надеюсь повезет пальцы crossed fingers"),
                ("🫰", "Сердечко пальцами", "hands", "сердечко корейское k-pop kpop любовь finger heart"),
                ("🤙", "Шака", "hands", "звони шака на связи звонок call me shaka"),
                ("🤌", "Итальянский жест", "hands", "что ты несешь мамма миа жест пальцы pinched fingers"),
                ("👌", "Окей", "hands", "ок хорошо идеально супер отлично perfect ok"),
                ("🙌", "Руки вверх", "hands", "ура хвала победа ладони празднование praise celebrate"),
                ("🫶", "Руки сердцем", "hands", "сердце руками любовь сердечко забота heart hands"),
                ("💪", "Мускул", "hands", "сила мощь спорт качалка красава power strong"),
                ("👊", "Удар кулаком", "hands", "кулак кулачок респект бро fist bump"),
                ("🤛", "Кулак влево", "hands", "кулак респект брат fist"),
                ("🤜", "Кулак вправо", "hands", "кулак встреча удар fist"),
                ("👐", "Открытые ладони", "hands", "ладони открыто искренность open hands"),
                ("☝️", "Указательный вверх", "hands", "внимание наверх слушай point up"),

                // Hearts & Romance
                ("💖", "Искрящееся сердце", "hearts", "розовое сердце блеск любовь sparkle heart"),
                ("❤️‍🔥", "Горящее сердце", "hearts", "страсть пылает огонь любовь сердце fire heart"),
                ("💘", "Сердце со стрелой", "hearts", "купидон стрела любовь влюбился arrow heart"),
                ("💝", "Сердце с бантом", "hearts", "подарок сердце бантик gift heart"),
                ("💞", "Кружащиеся сердца", "hearts", "сердца пара любовь revolving hearts"),
                ("💕", "Два сердца", "hearts", "два сердца любовь нежность two hearts"),
                ("💔", "Разбитое сердце", "hearts", "боль разбито тоска печаль heartbreak грустно"),
                ("❤️‍🩹", "Залеченное сердце", "hearts", "исцеление сердце пластырь выздоровление"),
                ("🧡", "Оранжевое сердце", "hearts", "оранжевый апельсин тепло orange heart"),
                ("💛", "Жёлтое сердце", "hearts", "желтый дружба солнце yellow heart"),
                ("💚", "Зелёное сердце", "hearts", "зеленый жизнь природа green heart"),
                ("💙", "Синее сердце", "hearts", "синий преданность мир blue heart"),
                ("💜", "Фиолетовое сердце", "hearts", "фиолетовый магия стиль purple heart"),
                ("🖤", "Чёрное сердце", "hearts", "черный темный стиль black heart"),
                ("🤍", "Белое сердце", "hearts", "белый чистота покой white heart"),
                ("💋", "След от поцелуя", "hearts", "поцелуй губы чмок kiss lips"),

                // Symbols & Objects
                ("🎯", "Мишень", "symbols", "в яблочко точно в цель target метко факт база"),
                ("🚀", "Ракета", "symbols", "взлет ракета rocket пушка старт космос туземун"),
                ("👑", "Корона", "symbols", "король королева царь king crown чемпион лучший босс"),
                ("💎", "Алмаз", "symbols", "бриллиант камень алмаз сокровище дорого diamond gem"),
                ("🏆", "Кубок", "symbols", "победа первое место чемпион кубок трофей trophy winner"),
                ("✨", "Блёстки", "symbols", "блеск магия искры sparkles чистота огонь"),
                ("🎃", "Тыква", "symbols", "хэллоуин тыква halloween праздник тыквочка"),
                ("👀", "Глаза", "symbols", "глазки смотрю вижу взгляд спалил интересно look see"),
                ("🙈", "Обезьяна закрыла глаза", "symbols", "стыдно не смотрю смущение обезьянка ой see no evil"),
                ("🍕", "Пицца", "symbols", "еда вкуснятина пицца обед перекус pizza food"),
                ("☕", "Кофе", "symbols", "чай чашка кофе утро бодрость coffee tea"),
                ("🍻", "Кружки пива", "symbols", "пиво за встречу тост выпьем чокаемся cheers beer"),
                ("🍿", "Попкорн", "symbols", "кино фильм попкорн смотрю кинотеатр popcorn movie"),
                ("🎵", "Нота", "symbols", "музыка трек песня звук мелодия music song"),
                ("☀️", "Солнце", "symbols", "солнце тепло день ясно погода sun"),
                ("🌙", "Полумесяц", "symbols", "луна ночь темно вечер moon")
            };

            _allReactions.Clear();
            foreach (var item in list)
            {
                _allReactions.Add(new ReactionItem
                {
                    Character = item.Character,
                    Name = item.Name,
                    Category = item.Category,
                    Keywords = item.Keywords
                });
            }

            ApplyFilter();
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cat)
            {
                _currentCategory = cat;

                // Update visual tabs
                foreach (var child in CategoriesPanel.Children)
                {
                    if (child is Button b)
                    {
                        bool isSelected = string.Equals(b.Tag as string, _currentCategory, StringComparison.OrdinalIgnoreCase);
                        b.Background = isSelected ? new SolidColorBrush(Color.FromRgb(37, 78, 130)) : new SolidColorBrush(Color.FromRgb(24, 36, 54));
                        b.Foreground = isSelected ? Brushes.White : (Brush)FindResource("TextSecondaryBrush");
                    }
                }

                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            string query = TxtFilter?.Text.Trim() ?? string.Empty;
            _displayedReactions.Clear();

            IEnumerable<ReactionItem> items = _allReactions;

            // Apply category filter if no text search
            if (string.IsNullOrWhiteSpace(query))
            {
                if (_currentCategory != "all")
                {
                    items = items.Where(r => r.Category == _currentCategory || (_currentCategory == "top" && r.Category == "top"));
                }
            }
            else
            {
                // In search mode, search across all categories
                items = items.Where(r =>
                    r.Character.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in items)
            {
                _displayedReactions.Add(item);
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtFilter.Text.Trim();
            PlaceholderFilter.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            BtnClearFilter.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
            TxtFilter.Focus();
        }

        private async void ReactionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string emoji && !string.IsNullOrWhiteSpace(emoji))
            {
                // Tactile scale-up animation on click (like mobile haptic pop)
                try
                {
                    if (fe is Button btn && btn.Template?.FindName("Bd", btn) is Border border)
                    {
                        var anim = new DoubleAnimation(1.4, TimeSpan.FromMilliseconds(80))
                        {
                            AutoReverse = true
                        };
                        border.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                        border.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                        await Task.Delay(60);
                    }
                }
                catch
                {
                    // Fallback to instant selection
                }

                ReactionSelected?.Invoke(this, emoji);
            }
        }

        private void BtnMoreEmojis_Click(object sender, RoutedEventArgs e)
        {
            OpenFullEmojiPickerRequested?.Invoke(this, EventArgs.Empty);
        }

        public void FocusSearch()
        {
            TxtFilter?.Focus();
        }

        public void ClosePopup()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            if (Parent is FrameworkElement fe && fe.Parent is System.Windows.Controls.Primitives.Popup p)
            {
                p.IsOpen = false;
            }
            else if (Parent is System.Windows.Controls.Primitives.Popup directP)
            {
                directP.IsOpen = false;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            ClosePopup();
        }

        private void TxtFilter_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (!string.IsNullOrEmpty(TxtFilter.Text))
                {
                    TxtFilter.Text = string.Empty;
                }
                else
                {
                    ClosePopup();
                }
                e.Handled = true;
            }
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                ClosePopup();
                e.Handled = true;
            }
        }

        public void ResetSearch()
        {
            TxtFilter.Text = string.Empty;
            _currentCategory = "all";
            foreach (var child in CategoriesPanel.Children)
            {
                if (child is Button b)
                {
                    bool isSelected = string.Equals(b.Tag as string, "all", StringComparison.OrdinalIgnoreCase);
                    b.Background = isSelected ? new SolidColorBrush(Color.FromRgb(37, 78, 130)) : new SolidColorBrush(Color.FromRgb(24, 36, 54));
                    b.Foreground = isSelected ? Brushes.White : (Brush)FindResource("TextSecondaryBrush");
                }
            }
            ApplyFilter();
        }
    }
}
