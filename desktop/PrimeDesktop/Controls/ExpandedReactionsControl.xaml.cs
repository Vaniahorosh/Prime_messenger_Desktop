using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PrimeDesktop.Controls
{
    public class ReactionItem
    {
        public string Character { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;
    }

    public partial class ExpandedReactionsControl : UserControl
    {
        public event EventHandler<string>? ReactionSelected;
        public event EventHandler? OpenFullEmojiPickerRequested;

        private readonly List<ReactionItem> _allReactions = new();
        private readonly ObservableCollection<ReactionItem> _displayedReactions = new();

        public ExpandedReactionsControl()
        {
            InitializeComponent();
            InitReactions();
            ReactionsItemsControl.ItemsSource = _displayedReactions;
        }

        private void InitReactions()
        {
            var list = new (string Character, string Name, string Keywords)[]
            {
                ("👍", "Палец вверх", "лайк like класс хорошо да yes plus"),
                ("👎", "Палец вниз", "дизлайк dislike против нет no minus"),
                ("❤️", "Красное сердце", "любовь love heart like обожаю"),
                ("🔥", "Огонь", "жара огонь fire hot топ top круто пушка"),
                ("🥰", "Влюблен", "сердца мило love cute обожаю"),
                ("👏", "Аплодисменты", "браво clap applause поздравляю"),
                ("😁", "Широкая улыбка", "улыбка радость grin happy позитив"),
                ("🤔", "Задумался", "думаю мысли think hmm хм вопрос"),
                ("🤯", "Взрыв мозга", "шок взрыв mindblown boom офигеть вау"),
                ("😱", "Испуг", "крик страх scream omg ужас шок"),
                ("🤬", "Злость", "ругань мат angry rage бесит"),
                ("😢", "Слезы", "грусть печаль cry sad обидно плачу"),
                ("🎉", "Праздник", "хлопушка салют party celebrate ура"),
                ("🤩", "В восторге", "звезды star eyes восторг супер"),
                ("🤮", "Тошнота", "фу блев vomit sick мерзко"),
                ("💩", "Какашка", "говно poop shit прикол шутка"),
                ("🙏", "Молитва", "спасибо пожалуйста pray thanks мольба"),
                ("👌", "Окей", "ок хорошо ok perfect супер"),
                ("🕊️", "Голубь мира", "мир птица dove peace спокойствие"),
                ("🤡", "Клоун", "цирк клоун clown шут дурак рофл"),
                ("🥱", "Зевок", "сон зевать yawn tired скучно"),
                ("🥴", "Опьянение", "пьяный woozy дурно косой"),
                ("😍", "Влюбленные глаза", "влюблен heart eyes love восторг"),
                ("🐳", "Кит", "кит whale море синий плывем"),
                ("❤️‍🔥", "Пылающее сердце", "страсть fire heart огонь пламя"),
                ("🌚", "Новолуние с лицом", "луна moon кринж прикол взгляд"),
                ("🌭", "Хот-дог", "еда сосиска hotdog food перекус"),
                ("💯", "Сто очков", "сотня 100 hundred база факт truth"),
                ("🤣", "Катается со смеху", "ржу смех rofl lmfao угар"),
                ("⚡", "Молния", "электричество ток lightning speed молния"),
                ("🍌", "Банан", "фрукт banana прикол"),
                ("🏆", "Кубок", "победа трофей win champion чемпион"),
                ("💔", "Разбитое сердце", "боль разбито heartbroken тоска"),
                ("🤨", "Подозрение", "бровь скептик rock doubt сомнение"),
                ("😐", "Нейтральное лицо", "без эмоций neutral покерфейс покер"),
                ("🍓", "Клубника", "ягода strawberry ягоды вкусно"),
                ("🍾", "Шампанское", "бутылка праздник champagne салют тост"),
                ("💋", "Поцелуй", "губы kiss поцелуйчик чмок"),
                ("🖕", "Средний палец", "фак middle finger фак ю жест"),
                ("😈", "Дьяволенок", "чертенок devil evil хитрец шалость"),
                ("😴", "Спящий", "сон zzz sleep спать спокойной"),
                ("😭", "Рыдания", "сильный плач sob crying рыдать слезы"),
                ("🤓", "Ботаник", "очки nerd умный зануда учеба"),
                ("👻", "Привидение", "призрак ghost бу буу мистика"),
                ("👨‍💻", "Программист", "разработчик кодер coder dev ит it"),
                ("👀", "Глаза", "глазки смотрю look see спалил наблюдаю"),
                ("🎃", "Тыква", "хэллоуин pumpkin halloween праздник"),
                ("🚀", "Ракета", "взлет пушка rocket fly космос старт")
            };

            _allReactions.Clear();
            _displayedReactions.Clear();
            foreach (var item in list)
            {
                var r = new ReactionItem { Character = item.Character, Name = item.Name, Keywords = item.Keywords };
                _allReactions.Add(r);
                _displayedReactions.Add(r);
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtFilter.Text.Trim();
            PlaceholderFilter.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            BtnClearFilter.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            _displayedReactions.Clear();
            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (var item in _allReactions) _displayedReactions.Add(item);
            }
            else
            {
                var filtered = _allReactions.Where(r =>
                    r.Character.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase));

                foreach (var item in filtered) _displayedReactions.Add(item);
            }
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
            TxtFilter.Focus();
        }

        private void ReactionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string emoji && !string.IsNullOrWhiteSpace(emoji))
            {
                ReactionSelected?.Invoke(this, emoji);
            }
        }

        private void BtnMoreEmojis_Click(object sender, RoutedEventArgs e)
        {
            OpenFullEmojiPickerRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ResetSearch()
        {
            TxtFilter.Text = string.Empty;
        }
    }
}
