using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services.Bot;

public sealed class BotLocalizationCatalog
{
    private static readonly IReadOnlyDictionary<string, BotLocale> Locales =
        new Dictionary<string, BotLocale>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new(
                "en", "🇬🇧", "English",
                "Start", "Stop", "Subscriptions", "Delete all", "Language",
                "Confirm stop", "Yes, delete all", "Yes, delete", "Cancel", "Refresh list", "Pause all", "Delete",
                "Choose an action from the menu below.",
                "Bot is active. Send a Telegram channel link or open your subscriptions list.",
                "Resumed {0} subscriptions. Send a Telegram channel link or open your subscriptions list.",
                "Pause all subscriptions?",
                "Confirm pause or cancel.",
                "Delete all subscriptions?",
                "Confirmation required.",
                "Confirm subscription deletion.",
                "Usage: /remove <channel>",
                "Subscription disabled for {0}.",
                "Subscription added: {0}",
                "Resumed {0} subscriptions.",
                "No active changes. The bot is already running.",
                "Done.",
                "Pause applied.",
                "Paused {0} subscriptions.",
                "There are no active subscriptions to pause.",
                "Deletion completed.",
                "Deleted {0} subscriptions.",
                "There are no subscriptions to delete.",
                "Action cancelled.",
                "Cancelled.",
                "Unknown action.",
                "Unknown action.",
                "Could not recognize the subscription.",
                "Error.",
                "Subscription not found.",
                "Not found.",
                "Subscription deleted.",
                "Deleted.",
                "Delete subscription {0}?",
                "No subscriptions yet. Send a channel link to add one.",
                "Subscriptions list updated.",
                "Your subscriptions:",
                "Choose your language:",
                "Language updated to {0}."),
            ["es"] = new(
                "es", "🇪🇸", "Español",
                "Iniciar", "Detener", "Suscripciones", "Eliminar todas", "Idioma",
                "Confirmar detención", "Sí, eliminar todas", "Sí, eliminar", "Cancelar", "Actualizar lista", "Pausar todo", "Eliminar",
                "Elige una acción del menú abajo.",
                "El bot está activo. Envía un enlace de canal de Telegram o abre tu lista de suscripciones.",
                "Se reanudaron {0} suscripciones. Envía un enlace de canal de Telegram o abre tu lista de suscripciones.",
                "¿Pausar todas las suscripciones?",
                "Confirma la pausa o cancela.",
                "¿Eliminar todas las suscripciones?",
                "Se requiere confirmación.",
                "Confirma la eliminación de la suscripción.",
                "Uso: /remove <channel>",
                "Suscripción desactivada para {0}.",
                "Suscripción agregada: {0}",
                "Se reanudaron {0} suscripciones.",
                "No hay cambios activos. El bot ya está funcionando.",
                "Hecho.",
                "Pausa aplicada.",
                "Se pausaron {0} suscripciones.",
                "No hay suscripciones activas para pausar.",
                "Eliminación completada.",
                "Se eliminaron {0} suscripciones.",
                "No hay suscripciones para eliminar.",
                "Acción cancelada.",
                "Cancelado.",
                "Acción desconocida.",
                "Acción desconocida.",
                "No se pudo reconocer la suscripción.",
                "Error.",
                "Suscripción no encontrada.",
                "No encontrada.",
                "Suscripción eliminada.",
                "Eliminada.",
                "¿Eliminar la suscripción {0}?",
                "Aún no hay suscripciones. Envía un enlace de canal para agregar una.",
                "Lista de suscripciones actualizada.",
                "Tus suscripciones:",
                "Elige tu idioma:",
                "Idioma cambiado a {0}."),
            ["pt"] = new(
                "pt", "🇵🇹", "Português",
                "Iniciar", "Parar", "Assinaturas", "Excluir todas", "Idioma",
                "Confirmar parada", "Sim, excluir todas", "Sim, excluir", "Cancelar", "Atualizar lista", "Pausar tudo", "Excluir",
                "Escolha uma ação no menu abaixo.",
                "O bot está ativo. Envie um link de canal do Telegram ou abra sua lista de assinaturas.",
                "Retomadas {0} assinaturas. Envie um link de canal do Telegram ou abra sua lista de assinaturas.",
                "Pausar todas as assinaturas?",
                "Confirme a pausa ou cancele.",
                "Excluir todas as assinaturas?",
                "Confirmação necessária.",
                "Confirme a exclusão da assinatura.",
                "Uso: /remove <channel>",
                "Assinatura desativada para {0}.",
                "Assinatura adicionada: {0}",
                "Retomadas {0} assinaturas.",
                "Nenhuma mudança ativa. O bot já está em execução.",
                "Concluído.",
                "Pausa aplicada.",
                "Pausadas {0} assinaturas.",
                "Não há assinaturas ativas para pausar.",
                "Exclusão concluída.",
                "Excluídas {0} assinaturas.",
                "Não há assinaturas para excluir.",
                "Ação cancelada.",
                "Cancelado.",
                "Ação desconhecida.",
                "Ação desconhecida.",
                "Não foi possível reconhecer a assinatura.",
                "Erro.",
                "Assinatura não encontrada.",
                "Não encontrada.",
                "Assinatura excluída.",
                "Excluída.",
                "Excluir a assinatura {0}?",
                "Ainda não há assinaturas. Envie um link de canal para adicionar uma.",
                "Lista de assinaturas atualizada.",
                "Suas assinaturas:",
                "Escolha seu idioma:",
                "Idioma alterado para {0}."),
            ["fr"] = new(
                "fr", "🇫🇷", "Français",
                "Démarrer", "Arrêter", "Abonnements", "Tout supprimer", "Langue",
                "Confirmer l'arrêt", "Oui, tout supprimer", "Oui, supprimer", "Annuler", "Actualiser la liste", "Tout mettre en pause", "Supprimer",
                "Choisissez une action dans le menu ci-dessous.",
                "Le bot est actif. Envoyez un lien de chaîne Telegram ou ouvrez votre liste d'abonnements.",
                "{0} abonnements repris. Envoyez un lien de chaîne Telegram ou ouvrez votre liste d'abonnements.",
                "Mettre tous les abonnements en pause ?",
                "Confirmez la pause ou annulez.",
                "Supprimer tous les abonnements ?",
                "Confirmation requise.",
                "Confirmez la suppression de l'abonnement.",
                "Utilisation : /remove <channel>",
                "Abonnement désactivé pour {0}.",
                "Abonnement ajouté : {0}",
                "{0} abonnements repris.",
                "Aucun changement actif. Le bot fonctionne déjà.",
                "Terminé.",
                "Pause appliquée.",
                "{0} abonnements mis en pause.",
                "Aucun abonnement actif à mettre en pause.",
                "Suppression terminée.",
                "{0} abonnements supprimés.",
                "Aucun abonnement à supprimer.",
                "Action annulée.",
                "Annulé.",
                "Action inconnue.",
                "Action inconnue.",
                "Impossible de reconnaître l'abonnement.",
                "Erreur.",
                "Abonnement introuvable.",
                "Introuvable.",
                "Abonnement supprimé.",
                "Supprimé.",
                "Supprimer l'abonnement {0} ?",
                "Aucun abonnement pour le moment. Envoyez un lien de chaîne pour en ajouter un.",
                "Liste des abonnements mise à jour.",
                "Vos abonnements :",
                "Choisissez votre langue :",
                "Langue changée en {0}."),
            ["de"] = new(
                "de", "🇩🇪", "Deutsch",
                "Starten", "Stoppen", "Abos", "Alle löschen", "Sprache",
                "Stopp bestätigen", "Ja, alle löschen", "Ja, löschen", "Abbrechen", "Liste aktualisieren", "Alles pausieren", "Löschen",
                "Wähle eine Aktion aus dem Menü unten.",
                "Der Bot ist aktiv. Sende einen Telegram-Kanallink oder öffne deine Abo-Liste.",
                "{0} Abos wurden fortgesetzt. Sende einen Telegram-Kanallink oder öffne deine Abo-Liste.",
                "Alle Abos pausieren?",
                "Pause bestätigen oder abbrechen.",
                "Alle Abos löschen?",
                "Bestätigung erforderlich.",
                "Löschen des Abos bestätigen.",
                "Verwendung: /remove <channel>",
                "Abo für {0} deaktiviert.",
                "Abo hinzugefügt: {0}",
                "{0} Abos wurden fortgesetzt.",
                "Keine aktiven Änderungen. Der Bot läuft bereits.",
                "Fertig.",
                "Pause angewendet.",
                "{0} Abos pausiert.",
                "Keine aktiven Abos zum Pausieren.",
                "Löschen abgeschlossen.",
                "{0} Abos gelöscht.",
                "Keine Abos zum Löschen vorhanden.",
                "Aktion abgebrochen.",
                "Abgebrochen.",
                "Unbekannte Aktion.",
                "Unbekannte Aktion.",
                "Abo konnte nicht erkannt werden.",
                "Fehler.",
                "Abo nicht gefunden.",
                "Nicht gefunden.",
                "Abo gelöscht.",
                "Gelöscht.",
                "Abo {0} löschen?",
                "Noch keine Abos. Sende einen Kanallink, um eines hinzuzufügen.",
                "Abo-Liste aktualisiert.",
                "Deine Abos:",
                "Wähle deine Sprache:",
                "Sprache geändert zu {0}."),
            ["id"] = new(
                "id", "🇮🇩", "Indonesia",
                "Mulai", "Berhenti", "Langganan", "Hapus semua", "Bahasa",
                "Konfirmasi berhenti", "Ya, hapus semua", "Ya, hapus", "Batal", "Perbarui daftar", "Jeda semua", "Hapus",
                "Pilih tindakan dari menu di bawah.",
                "Bot aktif. Kirim tautan kanal Telegram atau buka daftar langganan Anda.",
                "{0} langganan dilanjutkan. Kirim tautan kanal Telegram atau buka daftar langganan Anda.",
                "Jeda semua langganan?",
                "Konfirmasi jeda atau batalkan.",
                "Hapus semua langganan?",
                "Perlu konfirmasi.",
                "Konfirmasi penghapusan langganan.",
                "Penggunaan: /remove <channel>",
                "Langganan untuk {0} dinonaktifkan.",
                "Langganan ditambahkan: {0}",
                "{0} langganan dilanjutkan.",
                "Tidak ada perubahan aktif. Bot sudah berjalan.",
                "Selesai.",
                "Jeda diterapkan.",
                "{0} langganan dijeda.",
                "Tidak ada langganan aktif untuk dijeda.",
                "Penghapusan selesai.",
                "{0} langganan dihapus.",
                "Tidak ada langganan untuk dihapus.",
                "Tindakan dibatalkan.",
                "Dibatalkan.",
                "Tindakan tidak dikenal.",
                "Tindakan tidak dikenal.",
                "Langganan tidak dapat dikenali.",
                "Kesalahan.",
                "Langganan tidak ditemukan.",
                "Tidak ditemukan.",
                "Langganan dihapus.",
                "Dihapus.",
                "Hapus langganan {0}?",
                "Belum ada langganan. Kirim tautan kanal untuk menambahkannya.",
                "Daftar langganan diperbarui.",
                "Langganan Anda:",
                "Pilih bahasa Anda:",
                "Bahasa diubah ke {0}."),
            ["tr"] = new(
                "tr", "🇹🇷", "Türkçe",
                "Başlat", "Durdur", "Abonelikler", "Tümünü sil", "Dil",
                "Durdurmayı onayla", "Evet, tümünü sil", "Evet, sil", "İptal", "Listeyi yenile", "Tümünü duraklat", "Sil",
                "Aşağıdaki menüden bir işlem seçin.",
                "Bot aktif. Bir Telegram kanal bağlantısı gönderin veya abonelik listenizi açın.",
                "{0} abonelik devam ettirildi. Bir Telegram kanal bağlantısı gönderin veya abonelik listenizi açın.",
                "Tüm abonelikler duraklatılsın mı?",
                "Duraklatmayı onaylayın ya da iptal edin.",
                "Tüm abonelikler silinsin mi?",
                "Onay gerekli.",
                "Abonelik silmeyi onaylayın.",
                "Kullanım: /remove <channel>",
                "{0} için abonelik kapatıldı.",
                "Abonelik eklendi: {0}",
                "{0} abonelik devam ettirildi.",
                "Aktif değişiklik yok. Bot zaten çalışıyor.",
                "Tamam.",
                "Duraklatma uygulandı.",
                "{0} abonelik duraklatıldı.",
                "Duraklatılacak aktif abonelik yok.",
                "Silme tamamlandı.",
                "{0} abonelik silindi.",
                "Silinecek abonelik yok.",
                "İşlem iptal edildi.",
                "İptal edildi.",
                "Bilinmeyen işlem.",
                "Bilinmeyen işlem.",
                "Abonelik tanınamadı.",
                "Hata.",
                "Abonelik bulunamadı.",
                "Bulunamadı.",
                "Abonelik silindi.",
                "Silindi.",
                "{0} aboneliği silinsin mi?",
                "Henüz abonelik yok. Eklemek için bir kanal bağlantısı gönderin.",
                "Abonelik listesi güncellendi.",
                "Abonelikleriniz:",
                "Dilinizi seçin:",
                "Dil {0} olarak değiştirildi."),
            ["pl"] = new(
                "pl", "🇵🇱", "Polski",
                "Start", "Stop", "Subskrypcje", "Usuń wszystkie", "Język",
                "Potwierdź zatrzymanie", "Tak, usuń wszystkie", "Tak, usuń", "Anuluj", "Odśwież listę", "Wstrzymaj wszystko", "Usuń",
                "Wybierz działanie z menu poniżej.",
                "Bot jest aktywny. Wyślij link do kanału Telegram lub otwórz listę subskrypcji.",
                "Wznowiono {0} subskrypcji. Wyślij link do kanału Telegram lub otwórz listę subskrypcji.",
                "Wstrzymać wszystkie subskrypcje?",
                "Potwierdź wstrzymanie lub anuluj.",
                "Usunąć wszystkie subskrypcje?",
                "Wymagane potwierdzenie.",
                "Potwierdź usunięcie subskrypcji.",
                "Użycie: /remove <channel>",
                "Subskrypcja dla {0} została wyłączona.",
                "Dodano subskrypcję: {0}",
                "Wznowiono {0} subskrypcji.",
                "Brak aktywnych zmian. Bot już działa.",
                "Gotowe.",
                "Wstrzymanie zastosowane.",
                "Wstrzymano {0} subskrypcji.",
                "Brak aktywnych subskrypcji do wstrzymania.",
                "Usuwanie zakończone.",
                "Usunięto {0} subskrypcji.",
                "Brak subskrypcji do usunięcia.",
                "Anulowano działanie.",
                "Anulowano.",
                "Nieznane działanie.",
                "Nieznane działanie.",
                "Nie udało się rozpoznać subskrypcji.",
                "Błąd.",
                "Nie znaleziono subskrypcji.",
                "Nie znaleziono.",
                "Subskrypcja usunięta.",
                "Usunięto.",
                "Usunąć subskrypcję {0}?",
                "Nie ma jeszcze subskrypcji. Wyślij link do kanału, aby ją dodać.",
                "Lista subskrypcji została zaktualizowana.",
                "Twoje subskrypcje:",
                "Wybierz język:",
                "Zmieniono język na {0}."),
            ["it"] = new(
                "it", "🇮🇹", "Italiano",
                "Avvia", "Ferma", "Iscrizioni", "Elimina tutte", "Lingua",
                "Conferma arresto", "Sì, elimina tutte", "Sì, elimina", "Annulla", "Aggiorna elenco", "Metti tutto in pausa", "Elimina",
                "Scegli un'azione dal menu qui sotto.",
                "Il bot è attivo. Invia un link a un canale Telegram o apri il tuo elenco iscrizioni.",
                "Sono state riattivate {0} iscrizioni. Invia un link a un canale Telegram o apri il tuo elenco iscrizioni.",
                "Mettere in pausa tutte le iscrizioni?",
                "Conferma la pausa o annulla.",
                "Eliminare tutte le iscrizioni?",
                "Conferma richiesta.",
                "Conferma l'eliminazione dell'iscrizione.",
                "Uso: /remove <channel>",
                "Iscrizione disattivata per {0}.",
                "Iscrizione aggiunta: {0}",
                "Sono state riattivate {0} iscrizioni.",
                "Nessuna modifica attiva. Il bot è già in esecuzione.",
                "Fatto.",
                "Pausa applicata.",
                "Messe in pausa {0} iscrizioni.",
                "Non ci sono iscrizioni attive da mettere in pausa.",
                "Eliminazione completata.",
                "Eliminate {0} iscrizioni.",
                "Non ci sono iscrizioni da eliminare.",
                "Azione annullata.",
                "Annullato.",
                "Azione sconosciuta.",
                "Azione sconosciuta.",
                "Impossibile riconoscere l'iscrizione.",
                "Errore.",
                "Iscrizione non trovata.",
                "Non trovata.",
                "Iscrizione eliminata.",
                "Eliminata.",
                "Eliminare l'iscrizione {0}?",
                "Non ci sono ancora iscrizioni. Invia un link al canale per aggiungerne una.",
                "Elenco iscrizioni aggiornato.",
                "Le tue iscrizioni:",
                "Scegli la tua lingua:",
                "Lingua cambiata in {0}."),
            ["uk"] = new(
                "uk", "🇺🇦", "Українська",
                "Старт", "Стоп", "Список підписок", "Видалити всі", "Мова",
                "Підтвердити стоп", "Так, видалити всі", "Так, видалити", "Скасувати", "Оновити список", "Поставити все на паузу", "Видалити",
                "Оберіть дію з меню нижче.",
                "Бот активний. Надішліть посилання на Telegram-канал або відкрийте список підписок.",
                "Поновив {0} підписок. Надішліть посилання на Telegram-канал або відкрийте список підписок.",
                "Поставити всі підписки на паузу?",
                "Підтвердіть зупинку або скасуйте.",
                "Видалити всі підписки?",
                "Потрібне підтвердження.",
                "Підтвердіть видалення підписки.",
                "Використання: /remove <channel>",
                "Підписку для {0} вимкнено.",
                "Підписку додано: {0}",
                "Поновив {0} підписок.",
                "Активних змін немає. Бот уже працює.",
                "Готово.",
                "Паузу застосовано.",
                "Поставив на паузу {0} підписок.",
                "Активних підписок для паузи немає.",
                "Видалення завершено.",
                "Видалено {0} підписок.",
                "Немає підписок для видалення.",
                "Дію скасовано.",
                "Скасовано.",
                "Невідома дія.",
                "Невідома дія.",
                "Не вдалося розпізнати підписку.",
                "Помилка.",
                "Підписку не знайдено.",
                "Не знайдено.",
                "Підписку видалено.",
                "Видалено.",
                "Видалити підписку {0}?",
                "Підписок ще немає. Надішліть посилання на канал, щоб додати його.",
                "Список оновлено.",
                "Ваші підписки:",
                "Оберіть мову:",
                "Мову змінено на {0}.")
        };

    private static readonly IReadOnlyList<BotLanguageOption> SupportedLanguages = Locales.Values
        .Select(locale => new BotLanguageOption(locale.Code, locale.Flag, locale.Name))
        .ToList();

    public BotLocale GetLocale(string? languageCode)
    {
        var normalizedCode = NormalizeLanguageCode(languageCode);
        return Locales[normalizedCode];
    }

    public IReadOnlyList<BotLanguageOption> GetSupportedLanguages() => SupportedLanguages;

    public string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "en";
        }

        var code = languageCode.Trim().ToLowerInvariant();
        var primary = code.Split('-', '_', StringSplitOptions.RemoveEmptyEntries)[0];
        return Locales.ContainsKey(primary) ? primary : "en";
    }

    public bool TryResolveMainMenuAction(string? text, out BotMainMenuAction action)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            action = BotMainMenuAction.None;
            return false;
        }

        foreach (var locale in Locales.Values)
        {
            if (string.Equals(text, locale.StartLabel, StringComparison.OrdinalIgnoreCase))
            {
                action = BotMainMenuAction.Start;
                return true;
            }

            if (string.Equals(text, locale.StopLabel, StringComparison.OrdinalIgnoreCase))
            {
                action = BotMainMenuAction.Stop;
                return true;
            }

            if (string.Equals(text, locale.ListLabel, StringComparison.OrdinalIgnoreCase))
            {
                action = BotMainMenuAction.List;
                return true;
            }

            if (string.Equals(text, locale.DeleteAllLabel, StringComparison.OrdinalIgnoreCase))
            {
                action = BotMainMenuAction.DeleteAll;
                return true;
            }

            if (string.Equals(text, BuildLanguageButtonLabel(locale.Code), StringComparison.OrdinalIgnoreCase))
            {
                action = BotMainMenuAction.Language;
                return true;
            }

            if (string.Equals(text, locale.LanguageLabel, StringComparison.OrdinalIgnoreCase))
            {
                action = BotMainMenuAction.Language;
                return true;
            }
        }

        action = BotMainMenuAction.None;
        return false;
    }

    public bool TryResolveLanguageSelection(string? text, out string languageCode)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            languageCode = string.Empty;
            return false;
        }

        var normalized = NormalizeUiText(text);
        foreach (var locale in Locales.Values)
        {
            var candidates = new[]
            {
                NormalizeUiText(locale.Name),
                NormalizeUiText($"{locale.Flag} {locale.Name}"),
                NormalizeUiText($"✓ {locale.Flag} {locale.Name}"),
                NormalizeUiText($"✓ {locale.Name}")
            };

            if (candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                languageCode = locale.Code;
                return true;
            }
        }

        languageCode = string.Empty;
        return false;
    }

    public bool IsReservedUiText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (string.Equals(text?.Trim(), MiniAppButtonLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsManagedChannelsRequestLabel(text))
        {
            return true;
        }

        if (TryResolveMainMenuAction(text, out _) || TryResolveLanguageSelection(text, out _))
        {
            return true;
        }

        return Locales.Values.Any(locale =>
            string.Equals(NormalizeUiText(text), NormalizeUiText(locale.LanguageSelectionPrompt), StringComparison.OrdinalIgnoreCase));
    }

    public string BuildLanguageButtonLabel(string? languageCode)
    {
        var locale = GetLocale(languageCode);
        return $"{locale.Flag} {locale.LanguageLabel}";
    }

    public string MiniAppButtonLabel => "Mini App";

    public string ManagedChannelsButtonLabel => "Add my channel";

    public bool IsManagedChannelsRequestLabel(string? text) =>
        string.Equals(text?.Trim(), ManagedChannelsButtonLabel, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUiText(string? value) =>
        (value ?? string.Empty)
            .Replace("✓", string.Empty, StringComparison.Ordinal)
            .Trim();

    public sealed record BotLanguageOption(string Code, string Flag, string Name);

    public sealed record BotLocale(
        string Code,
        string Flag,
        string Name,
        string StartLabel,
        string StopLabel,
        string ListLabel,
        string DeleteAllLabel,
        string LanguageLabel,
        string ConfirmStopLabel,
        string ConfirmDeleteAllLabel,
        string ConfirmDeleteOneLabel,
        string CancelLabel,
        string RefreshListLabel,
        string PauseAllLabel,
        string DeletePrefix,
        string EmptyUpdatePrompt,
        string StartMessageWithoutResumed,
        string StartMessageWithResumedTemplate,
        string PauseConfirmationPrompt,
        string PauseConfirmationCallbackNotice,
        string DeleteAllConfirmationPrompt,
        string DeleteAllConfirmationCallbackNotice,
        string DeleteOneConfirmationCallbackNotice,
        string RemoveUsage,
        string SubscriptionDisabledTemplate,
        string SubscriptionAddedTemplate,
        string StartCallbackWithResumedTemplate,
        string StartCallbackWithoutChanges,
        string StartCallbackNotice,
        string PauseAppliedNotice,
        string PauseAppliedTemplate,
        string PauseAppliedWithoutChanges,
        string DeletionCompletedNotice,
        string DeleteAllAppliedTemplate,
        string DeleteAllAppliedWithoutChanges,
        string ActionCancelledMessage,
        string ActionCancelledNotice,
        string UnknownActionMessage,
        string UnknownActionNotice,
        string InvalidSubscriptionMessage,
        string ErrorNotice,
        string SubscriptionNotFoundMessage,
        string SubscriptionNotFoundNotice,
        string SubscriptionDeletedMessage,
        string SubscriptionDeletedNotice,
        string DeleteOneConfirmationTemplate,
        string EmptySubscriptionsMessage,
        string SubscriptionsListUpdatedNotice,
        string SubscriptionsListTitle,
        string LanguageSelectionPrompt,
        string LanguageUpdatedTemplate);
}

public enum BotMainMenuAction
{
    None = 0,
    Start = 1,
    Stop = 2,
    List = 3,
    DeleteAll = 4,
    Language = 5
}
