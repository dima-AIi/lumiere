namespace Lumiere.Models;

/// <summary>Человекочитаемые подписи статусов для админки (единый источник, чтобы не дублировать в views).</summary>
public static class AdminLabels
{
    /// <summary>Статус оплаты → подпись.</summary>
    public static string Payment(string s) => s switch
    {
        "created" => "Создан",
        "pending" => "В обработке",
        "succeeded" => "Оплачен",
        "waiting_for_capture" => "Ожидает списания",
        "canceled" => "Отменён",
        "demo" => "Демо",
        "error" => "Ошибка",
        "mismatch" => "Несоответствие",
        _ => s
    };

    /// <summary>Статус выполнения/решения по заказу → подпись.</summary>
    public static string Kitchen(string s) => s switch
    {
        "new" => "Новый",
        "accepted" => "Принят",
        "cooking" => "Готовится",
        "ready" => "Готов",
        "done" => "Выдан",
        "declined" => "Отклонён",
        _ => s
    };
}
