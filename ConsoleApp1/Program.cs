using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static ITelegramBotClient _botClient;
    private static ReceiverOptions _receiverOptions;

    static async Task Main()
    {
        _botClient = new TelegramBotClient("8079992634:AAEVmQt0y9OidLJdxVYH8i2oj30xFR2ZmWg");
        _receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[]
            {
            UpdateType.Message,
            UpdateType.CallbackQuery
        },
        };

        using var cts = new CancellationTokenSource();
        var birthdayManager = new BirthdayManager();
        var holidayManager = new HolidayManager(_botClient); 
        var handler = new UpdateHandler(_botClient, birthdayManager, holidayManager); 

        _botClient.StartReceiving(handler.HandleUpdateAsync, ErrorHandler, _receiverOptions, cts.Token);

        var me = await _botClient.GetMeAsync();
        Console.WriteLine($"{me.FirstName} запущен!");

        _ = holidayManager.NotifyHolidays();
        await Task.Delay(-1);
    }

    private static async Task UpdateHandler(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var birthdayManager = new BirthdayManager();
        var holidayManager = new HolidayManager(botClient);
        var handler = new UpdateHandler(botClient, birthdayManager, holidayManager);
        await handler.HandleUpdateAsync(botClient, update, cancellationToken);
    }

    private static Task ErrorHandler(ITelegramBotClient botClient, Exception error, CancellationToken cancellationToken)
    {
        var errorMessage = error switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => error.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }
}

public class UpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BirthdayManager _birthdayManager;
    private readonly HolidayManager _holidayManager;

    public UpdateHandler(ITelegramBotClient botClient, BirthdayManager birthdayManager, HolidayManager holidayManager)
    {
        _botClient = botClient;
        _birthdayManager = birthdayManager;
        _holidayManager = holidayManager;
    }
   
    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message:
                    await HandleMessage(update.Message);
                    break;

                case UpdateType.CallbackQuery:
                    await HandleCallbackQuery(update.CallbackQuery);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

    }

    private Dictionary<long, (string Name, DateTime? Birthday)> _userBirthdays = new 
    Dictionary<long, (string, DateTime?)>();
    private async Task HandleMessage(Message message)
    {
        var user = message.From;
        var chat = message.Chat;

        Console.WriteLine($"{user.FirstName} ({user.Id}) написал сообщение: {message.Text}");

        if (message.Type == MessageType.Text)
        {
            switch (message.Text)
            {
                case "/start":
                    await _botClient.SendTextMessageAsync(chat.Id, "Привет!Напишите одну из команд:\n/holiday\n/allholiday\n/addbirthday\n/listbirthdays");
                    break;

                case "/holiday":
                    await _holidayManager.CheckHolidayToday(chat.Id);
                    break;

                case "/allholiday":
                    await _holidayManager.SendAllHolidays(chat.Id);
                    break;

                case "/addbirthday":
                    await _botClient.SendTextMessageAsync(chat.Id, "Введите команду в формате: /addbirthday Имя ГГГГ-ММ-ДД.");
                    break;

                case var text when text.StartsWith("/addbirthday"):
                    await AddBirthday(chat.Id, text);
                    break;

                case "/listbirthdays":
                    Console.WriteLine($"Запрос на список дней рождения от chatId {chat.Id}");
                    await SendAllBirthdays(chat.Id);
                    break;
            }
        }
    }

    private async Task AddBirthday(long chatId, string messageText)
    {
        var input = messageText.Replace("/addbirthday ", "").Trim();
        var parts = input.Split(new[] { ' ' }, 2);

        if (parts.Length < 2)
        {
            await _botClient.SendTextMessageAsync(chatId, "Пожалуйста, введите команду в формате: /addbirthday Имя ГГГГ-ММ-ДД.");
            return;
        }

        var name = parts[0];
        var dateInput = parts[1];

        if (DateTime.TryParseExact(dateInput, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime birthday))
        {
            var existingBirthdays = _birthdayManager.GetBirthdays(chatId);
            var existingBirthday = existingBirthdays.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && b.Date.Date == birthday.Date);

            if (existingBirthday != null)
            {
                await _botClient.SendTextMessageAsync(chatId,
                    $"День рождения {name} ({birthday.ToShortDateString()}) уже добавлен. ");
                return;
            }

            _birthdayManager.AddBirthday(chatId, name, birthday);
            await _botClient.SendTextMessageAsync(chatId, $"День рождения {name} ({birthday.ToShortDateString()}) добавлен.");
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "Неверный формат даты. Пожалуйста, используйте формат: ГГГГ-ММ-ДД.");
        }
    }

    private async Task SendAllBirthdays(long chatId)
    {
        var birthdays = _birthdayManager.GetBirthdays(chatId);
        Console.WriteLine($"Запрос на получение дней рождения для chatId {chatId}. Найдено: {birthdays.Count}");

        if (birthdays.Count > 0)
        {
            var birthdayList = string.Join(", ", birthdays.Select(b => $"{b.Name} ({b.Date.ToShortDateString()})"));
            await _botClient.SendTextMessageAsync(chatId, $"Дни рождения: {birthdayList}");
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "У вас нет добавленных дней рождения.");
        }
    }
    //это реплай клава, алина не трогай я потом грохну
    private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        var chatId = callbackQuery.Message.Chat.Id;

        if (callbackQuery.Data == "back")
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы вернулись в основное меню.");
        }
    }
}
public class BirthdayEntry
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public long ChatId { get; set; }

    public BirthdayEntry(string name, DateTime date)
    {
        Name = name;
        Date = date;
    }
}

public class BirthdayManager
{
    private Dictionary<long, List<BirthdayEntry>> userBirthdays = new Dictionary<long, List<BirthdayEntry>>();

    public void AddBirthday(long chatId, string name, DateTime birthday)
    {
        if (!userBirthdays.ContainsKey(chatId))
        {
            userBirthdays[chatId] = new List<BirthdayEntry>();
        }
        userBirthdays[chatId].Add(new BirthdayEntry(name, birthday));
    }

    public List<BirthdayEntry> GetBirthdays(long chatId)
    {
        return userBirthdays.ContainsKey(chatId) ? userBirthdays[chatId] : new List<BirthdayEntry>();
    }
    public List<BirthdayEntry> GetAllBirthdays()
    {
        var allBirthdays = new List<BirthdayEntry>();

        foreach (var birthdays in userBirthdays.Values)
        {
            allBirthdays.AddRange(birthdays);
        }

        return allBirthdays;
    }
}

public class Holiday
{
    public DateTime Date { get; set; }
    public string LocalName { get; set; }
    public string Name { get; set; }
    public string CountryCode { get; set; }
}

public class HolidayManager
{
    private static HashSet<DateTime> _holidays = new HashSet<DateTime>();
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly ITelegramBotClient _botClient;

    public HolidayManager(ITelegramBotClient botClient)
    {
        _botClient = botClient;
    }

    public async Task NotifyHolidays()
    {
        while (true)
        {
            await LoadHolidays(DateTime.Now.Year, "RU");
            var today = DateTime.Now.Date;

            if (_holidays.Contains(today))
            {
                long chatId = 1973718680;
                await _botClient.SendTextMessageAsync(chatId, "Сегодня праздник!");
            }

            await Task.Delay(TimeSpan.FromDays(1));
        }
    }

    public static async Task LoadHolidays(int year, string countryCode)
    {
        var response = await _httpClient.GetAsync($"https://date.nager.at/api/v2/PublicHolidays/2024/RU");
        if (response.IsSuccessStatusCode)
        {
            var holidays = await response.Content.ReadFromJsonAsync<List<Holiday>>();
            _holidays.Clear();
            foreach (var holiday in holidays)
            {
                _holidays.Add(holiday.Date);
            }
        }
    }
    public async Task CheckHolidayToday(long chatId)
    {
        var today = DateTime.Now.Date;

        if (_holidays.Contains(today))
        {
            await _botClient.SendTextMessageAsync(chatId, "Сегодня праздник!");
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "Сегодня нет праздника.");
        }
    }
    public async Task SendAllHolidays(long chatId)
    {
        var holidayList = string.Join(", ", _holidays.Select(h => h.ToShortDateString()));

        if (string.IsNullOrEmpty(holidayList))
        {
            await _botClient.SendTextMessageAsync(chatId, "Нет доступных праздников.");
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, $"Все праздники: {holidayList}");
        }
    }
}


