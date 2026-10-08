using System.Globalization;

namespace ManaUI
{
    /// <summary>A reusable local date/time picker with optional calendar annotations and popup content.</summary>
    public sealed class ManaDateTimePickerWidget : Button
    {
        private readonly UiTheme _theme;
        private DateTimeOffset _value = DateTimeOffset.Now;

        public event EventHandler<DateTimeOffset>? ValueChanged;

        public DateTimeOffset Value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                InvalidateVisual();
                ValueChanged?.Invoke(this, value);
            }
        }

        public bool ShowMoonPhaseEmojis { get; set; } = true;
        public bool ShowPhaseName { get; set; } = true;
        public bool ShowTimeControls { get; set; } = true;
        public Func<DateTimeOffset, string?>? MoonPhaseEmojiProvider { get; set; }
        public Func<DateTimeOffset, string?>? CalendarDayEmojiProvider { get; set; }
        public Func<DateTimeOffset, string?>? MoonPhaseNameProvider { get; set; }
        public Func<UiTheme, UiElement?>? AdditionalContentFactory { get; set; }

        public ManaDateTimePickerWidget(UiTheme theme)
        {
            _theme = theme;
            Width = 142.0f;
            this.BindPolled(button => button.Text, () => Value.ToString("MMM d, yyyy HH:mm", CultureInfo.CurrentCulture));
            this.WithTooltip("Choose a date and time.");
            Click += (_, _) => ShowPicker();
        }

        private void ShowPicker()
        {
            var root = Root;
            if (root == null)
            {
                return;
            }

            var selectedDate = DateOnly.FromDateTime(Value.Date);
            var displayedMonth = new DateOnly(selectedDate.Year, selectedDate.Month, 1);
            var hour = Value.Hour;
            var minute = Value.Minute;

            DateTimeOffset SelectedDateTime()
            {
                var local = new DateTime(selectedDate.Year, selectedDate.Month, selectedDate.Day, hour, minute, 0);
                return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            }

            DateOnly CalendarStart()
            {
                var first = new DateOnly(displayedMonth.Year, displayedMonth.Month, 1);
                return first.AddDays(-(int)first.DayOfWeek);
            }

            DateOnly CalendarDate(int index) => CalendarStart().AddDays(index);

            void ShiftTime(int minutes)
            {
                var changed = new DateTime(selectedDate.Year, selectedDate.Month, selectedDate.Day, hour, minute, 0)
                    .AddMinutes(minutes);
                selectedDate = DateOnly.FromDateTime(changed);
                displayedMonth = new DateOnly(changed.Year, changed.Month, 1);
                hour = changed.Hour;
                minute = changed.Minute;
            }

            var monthTitle = new TextBlock { Width = 200.0f, TextAlignment = TextAlignment.Center }.CenterV();
            monthTitle.BindTextPolled(() => displayedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
            var calendar = new StackPanel().Vertical(2.0f);
            calendar.Children.Add(new StackPanel().Horizontal(2.0f).Children(
                new Button("<") { Width = 32.0f }.WithTooltip("Previous month").OnClick(() => displayedMonth = displayedMonth.AddMonths(-1)),
                monthTitle,
                new Button(">") { Width = 32.0f }.WithTooltip("Next month").OnClick(() => displayedMonth = displayedMonth.AddMonths(1))));

            var weekdays = new StackPanel().Horizontal(2.0f);
            foreach (var weekday in new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" })
            {
                weekdays.Children.Add(new TextBlock().Text(weekday).Width(38.0f).Align(TextAlignment.Center).Foreground(_theme.TextMuted));
            }
            calendar.Children.Add(weekdays);

            for (var week = 0; week < 6; week++)
            {
                var row = new StackPanel().Horizontal(2.0f);
                for (var day = 0; day < 7; day++)
                {
                    var index = week * 7 + day;
                    var dayButton = new Button { Width = 38.0f, Height = 28.0f, IgnoreThemePadding = true };
                    dayButton.BindPolled(button => button.Text, () =>
                    {
                        var date = CalendarDate(index);
                        var localNoon = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0);
                        var dateTime = new DateTimeOffset(localNoon, TimeZoneInfo.Local.GetUtcOffset(localNoon));
                        var emoji = ShowMoonPhaseEmojis ? CalendarDayEmojiProvider?.Invoke(dateTime) : null;
                        var dayLabel = date.Day.ToString(CultureInfo.InvariantCulture);
                        return string.IsNullOrEmpty(emoji) ? dayLabel : $"{dayLabel} {emoji}";
                    });
                    dayButton.BindPolled(button => button.Variant, () =>
                    {
                        var date = CalendarDate(index);
                        return date == selectedDate ? ButtonVariant.Primary :
                            date.Year == displayedMonth.Year && date.Month == displayedMonth.Month ? ButtonVariant.Subtle : ButtonVariant.Standard;
                    });
                    dayButton.Click += (_, _) =>
                    {
                        selectedDate = CalendarDate(index);
                        displayedMonth = new DateOnly(selectedDate.Year, selectedDate.Month, 1);
                    };
                    row.Children.Add(dayButton);
                }
                calendar.Children.Add(row);
            }

            var hourValue = new TextBlock { Width = 24.0f, TextAlignment = TextAlignment.Center }.CenterV();
            hourValue.BindTextPolled(() => hour.ToString("00", CultureInfo.InvariantCulture));
            var minuteValue = new TextBlock { Width = 24.0f, TextAlignment = TextAlignment.Center }.CenterV();
            minuteValue.BindTextPolled(() => minute.ToString("00", CultureInfo.InvariantCulture));
            var timeControls = new StackPanel().Horizontal(2.0f).Children(
                new Button("-").Width(28.0f).WithTooltip("Earlier hour").OnClick(() => ShiftTime(-60)),
                hourValue,
                new Button("+").Width(28.0f).WithTooltip("Later hour").OnClick(() => ShiftTime(60)),
                new TextBlock().Text(":").CenterV(),
                new Button("-").Width(28.0f).WithTooltip("Earlier minute").OnClick(() => ShiftTime(-1)),
                minuteValue,
                new Button("+").Width(28.0f).WithTooltip("Later minute").OnClick(() => ShiftTime(1)));
            timeControls.BindPolled(controls => controls.Visibility, () => ShowTimeControls ? Visibility.Visible : Visibility.Collapsed);

            var phase = new TextBlock { Foreground = _theme.TextMuted };
            phase.BindTextPolled(() =>
            {
                var selected = SelectedDateTime();
                var parts = new List<string>();
                if (ShowMoonPhaseEmojis && MoonPhaseEmojiProvider?.Invoke(selected) is { Length: > 0 } emoji)
                {
                    parts.Add(emoji);
                }
                if (ShowPhaseName && MoonPhaseNameProvider?.Invoke(selected) is { Length: > 0 } name)
                {
                    parts.Add(name);
                }
                return string.Join("  ", parts);
            });
            phase.BindPolled(text => text.Visibility, () =>
                (ShowMoonPhaseEmojis && MoonPhaseEmojiProvider != null) || (ShowPhaseName && MoonPhaseNameProvider != null)
                    ? Visibility.Visible
                    : Visibility.Collapsed);

            var error = new TextBlock { Foreground = _theme.Danger, Visibility = Visibility.Collapsed };
            UiPopup? popup = null;
            var apply = new Button().Label("Set").Variant(ButtonVariant.Primary).OnClick(() =>
            {
                var localTime = new DateTime(selectedDate.Year, selectedDate.Month, selectedDate.Day, hour, minute, 0);
                if (TimeZoneInfo.Local.IsInvalidTime(localTime))
                {
                    error.Text = "This local time does not exist because of daylight saving time.";
                    error.Visibility = Visibility.Visible;
                    return;
                }

                Value = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
                popup?.Close();
            });

            var cancel = new Button().Label("Cancel").Variant(ButtonVariant.Subtle).OnClick(() => popup?.Close());
            var content = new StackPanel().Vertical(_theme.Spacing).Padding(8.0f).Children(
                new TextBlock().Text("Choose a date"),
                calendar,
                new TextBlock().Text("Time (local)").Foreground(_theme.TextMuted).FontSize(_theme.FontSizeSmall),
                timeControls,
                phase);
            if (AdditionalContentFactory?.Invoke(_theme) is { } additionalContent)
            {
                content.Children.Add(additionalContent);
            }
            content.Children.Add(error);
            content.Children.Add(new StackPanel().Horizontal(_theme.Spacing).Children(apply, cancel));

            var popupContent = new Border().Fill(_theme.SurfaceRaised).Outline(_theme.BorderStrong, 1.0f).Rounded(_theme.CornerRadius).Shadow(16.0f)
                .Width(310.0f).Content(content);
            var pickerPopup = new UiPopup { Placement = PopupPlacement.Bottom, Child = popupContent };
            popup = pickerPopup;
            root.Popups.Show(pickerPopup, this);
        }
    }
}
