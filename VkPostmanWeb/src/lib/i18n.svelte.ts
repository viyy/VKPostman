// Tiny reactive i18n. Keys are the English source strings, so untranslated
// strings gracefully fall back to English. `t()` reads the runed `locale`,
// so any component that calls it re-renders when the language changes.

export type Locale = 'en' | 'ru';

const LS_KEY = 'vkp.lang';

const ru: Record<string, string> = {
  // ---- Tabs / top bar ----
  'Drafts': 'Черновики',
  'Templates': 'Шаблоны',
  'Placeholders': 'Поля',
  'Groups': 'Группы',
  'Stats': 'Статистика',
  'Search everything (Ctrl+K)': 'Искать везде (Ctrl+K)',
  'Light theme': 'Светлая тема',
  'Dark theme': 'Тёмная тема',
  'Data tools: export a selection · merge import': 'Данные: экспорт выборки · импорт-слияние',
  'Export all data (JSON backup)': 'Экспорт всех данных (резервная копия JSON)',
  'Import data — replace all (JSON)': 'Импорт данных — заменить всё (JSON)',
  'offline · local-only': 'офлайн · только локально',

  // ---- Banners ----
  'Export now': 'Экспортировать сейчас',
  'Remind me later': 'Напомнить позже',
  'Your data lives only in this browser.': 'Данные хранятся только в этом браузере.',
  'View agenda': 'Открыть план',
  'Dismiss': 'Скрыть',

  // ---- Common buttons ----
  'New': 'Создать',
  'Add': 'Добавить',
  'Close': 'Закрыть',
  'Save': 'Сохранить',
  'Cancel': 'Отмена',
  'Delete': 'Удалить',
  'Duplicate': 'Дублировать',
  'Select': 'Выбрать',
  'Done': 'Готово',
  'Export': 'Экспорт',
  'Clear': 'Очистить',
  'Open': 'Открыть',
  'Copy': 'Копировать',
  'Posted': 'Опубликовано',
  'Unmark': 'Снять отметку',
  'Loading…': 'Загрузка…',

  // ---- Autosave status ----
  'Saving…': 'Сохранение…',
  '✓ Saved': '✓ Сохранено',
  '⚠ Save failed': '⚠ Ошибка сохранения',

  // ---- Drafts list / filter ----
  'No drafts yet.': 'Черновиков пока нет.',
  'Search title, text, group…': 'Поиск по заголовку, тексту, группе…',
  'All': 'Все',
  'Pending': 'В работе',
  'No drafts match the current filter.': 'Нет черновиков по текущему фильтру.',
  '{n} selected': 'Выбрано: {n}',

  // ---- Draft details ----
  'Pick a draft on the left, or click': 'Выберите черновик слева или нажмите',
  'Draft details': 'Детали черновика',
  'ready': 'готово',
  'incomplete': 'не готово',
  'Pin to top': 'Закрепить вверху',
  'Unpin': 'Открепить',
  'Title': 'Заголовок',
  'Common text': 'Общий текст',
  'Theme tags (common to all groups)': 'Тематические теги (для всех групп)',
  'Images to attach': 'Изображения для прикрепления',
  '(filenames/paths — a manual checklist, files aren’t stored)':
    '(имена файлов/пути — ручной список, файлы не хранятся)',
  'Drop image files here to add their names': 'Перетащите файлы сюда, чтобы добавить их имена',
  '…or type a filename/path and press Enter': '…или введите имя файла/путь и нажмите Enter',
  'Scratch notes': 'Заметки',
  '(private, not posted)': '(приватно, не публикуется)',
  'Reminders, ideas, to-dos for this post…': 'Напоминания, идеи, задачи для этого поста…',
  'Plan to post on': 'Запланировать публикацию на',
  '(optional)': '(необязательно)',

  // ---- Validation ----
  'No target groups selected.': 'Не выбрано ни одной группы.',
  'Missing value: {name} ({groups})': 'Не заполнено: {name} ({groups})',

  // ---- Target groups ----
  'Target groups': 'Целевые группы',
  'No groups yet — add some on the': 'Групп пока нет — добавьте их на вкладке',
  'tab.': '.',
  'no template assigned': 'шаблон не назначен',
  'template:': 'шаблон:',
  'Filter by marker:': 'Фильтр по меткам:',
  'No groups match the selected markers.': 'Нет групп с выбранными метками.',

  // ---- Placeholders (draft) ----
  'union across selected groups\' templates': 'объединение по шаблонам выбранных групп',
  'used by: {names}': 'используется: {names}',

  // ---- To post ----
  'To post': 'К публикации',
  'Preview': 'Превью',
  'Raw': 'Исходник',
  'Show raw copyable text': 'Показать исходный текст для копирования',
  'Show VK-style preview (links)': 'Показать превью как во ВКонтакте (ссылки)',
  'Open all': 'Открыть все',
  'Copy next & open': 'Скопировать следующий и открыть',
  'Pick one or more target groups on the left to see rendered posts.':
    'Выберите одну или несколько групп слева, чтобы увидеть готовые посты.',
  'All selected groups are marked posted. 🎉': 'Все выбранные группы отмечены как опубликованные. 🎉',
  'Open vk.com': 'Открыть vk.com',
  '{n} chars': '{n} симв.',
  'exceeds ~{n}': 'превышает ~{n}',
  '{n} posted': 'опубликовано: {n}',
  'Posted to {a}/{b} groups': 'Опубликовано в {a}/{b} групп',
  'last on {date}': 'последняя {date}',
  'posted {date}': 'опубликовано {date}',
  'Drag to reorder': 'Перетащите для изменения порядка',

  // ---- Templates ----
  'No templates yet.': 'Шаблонов пока нет.',
  'Search name, group, alias…': 'Поиск по названию, группе, адресу…',
  'Pick a template on the left, or click': 'Выберите шаблон слева или нажмите',
  'Edit template': 'Редактирование шаблона',
  'Name': 'Название',
  'Description': 'Описание',
  'Body': 'Тело',
  'Live preview': 'Живое превью',
  '(sample values)': '(примерные значения)',
  'Default theme tags': 'Тематические теги по умолчанию',
  'Placeholders used': 'Используемые поля',
  'Insert:': 'Вставить:',
  'No matches. Type the name — a placeholder row will be added for you.':
    'Нет совпадений. Введите имя — поле будет создано автоматически.',
  'Scriban-like {code} syntax. New keys auto-appear on the Placeholders tab.':
    'Синтаксис {code} (как в Scriban). Новые ключи появляются на вкладке Поля.',
  'Optional blocks: {if} and {unless} — shown only when the value is filled.':
    'Необязательные блоки: {if} и {unless} — показываются, только если значение заполнено.',
  'Derived from the Body. Edit definitions on the Placeholders tab — changes propagate to every template using that key.':
    'Берётся из тела. Редактируйте определения на вкладке Поля — изменения применяются ко всем шаблонам с этим ключом.',
  'None yet. Type {code} in the Body to add one.':
    'Пока нет. Введите {code} в теле, чтобы добавить.',
  'VK link': 'ссылка VK',
  'wiki link': 'вики-ссылка',
  'URL': 'URL',
  'tags': 'теги',
  'text': 'текст',
  'Used by groups': 'Используется группами',
  'No groups use this template yet. Pick one above to link it.':
    'Этот шаблон пока не используется. Выберите группу выше, чтобы привязать.',
  'Link a group to this template…': 'Привязать группу к этому шаблону…',
  'Search groups…': 'Поиск групп…',
  'Delete template': 'Удалить шаблон',
  'Add:': 'Добавить:',

  // ---- Groups ----
  'No groups yet. Click': 'Групп пока нет. Нажмите',
  'to create one.': 'чтобы создать.',
  'Search name, alias, template…': 'Поиск по названию, адресу, шаблону…',
  'Pick a group on the left, or click': 'Выберите группу слева или нажмите',
  'Edit group': 'Редактирование группы',
  'Display name': 'Отображаемое имя',
  'Screen name': 'Короткий адрес',
  'Template': 'Шаблон',
  'Search templates…': 'Поиск шаблонов…',
  'A group without a template can\'t be a draft target.':
    'Группа без шаблона не может быть целью черновика.',
  'Mandatory tags (space-separated)': 'Обязательные теги (через пробел)',
  'Appended to posts via {code}.': 'Добавляется в посты через {code}.',
  'Markers (labels for organising/filtering)': 'Метки (для организации/фильтрации)',
  'Not posted — used to filter groups (e.g. on the Drafts page).':
    'Не публикуются — используются для фильтрации групп (например, на вкладке Черновики).',
  'Active': 'Активна',
  'Notes': 'Заметки',
  'Delete group': 'Удалить группу',
  'Open this template': 'Открыть этот шаблон',
  'Open this group': 'Открыть эту группу',

  // ---- Placeholders tab ----
  'Edit placeholder': 'Редактирование поля',
  'Delete placeholder': 'Удалить поле',
  'Remove unused ({n})': 'Удалить неиспользуемые ({n})',
  'Type': 'Тип',
  'Used by': 'Используется',
  'Pick a placeholder on the left, or click': 'Выберите поле слева или нажмите',
  'No placeholders yet. Type {code} in a template’s Body and one will appear here.':
    'Полей пока нет. Введите {code} в теле шаблона — поле появится здесь.',
  'Key (referenced as {code})': 'Ключ (используется как {code})',
  'Description (optional)': 'Описание (необязательно)',
  'Default value (used when the draft leaves this field empty)':
    'Значение по умолчанию (если поле в черновике пустое)',
  '(no templates reference this key)': '(ни один шаблон не использует этот ключ)',
  'Text': 'Текст',
  'VK link (@name)': 'Ссылка VK (@имя)',
  'Wiki link [target|display]': 'Вики-ссылка [цель|текст]',
  'Tag list': 'Список тегов',

  // ---- Stats ----
  'Agenda': 'План',
  'No planned posts. Set “Plan to post on” on a draft to see it here.':
    'Запланированных постов нет. Укажите дату в черновике, чтобы увидеть его здесь.',
  'Overdue': 'Просрочено',
  'Today': 'Сегодня',
  'This week': 'На этой неделе',
  'Later': 'Позже',
  'Overview': 'Обзор',
  'Posts marked': 'Отмечено постов',
  'Draft status': 'Статус черновиков',
  'Fully posted': 'Полностью опубликованы',
  'In progress': 'В работе',
  'No groups yet': 'Без групп',
  'Posting activity': 'Активность публикаций',
  'Last 7 days': 'За 7 дней',
  'Last 30 days': 'За 30 дней',
  'Posts per group': 'Посты по группам',
  'Template usage': 'Использование шаблонов',
  'None.': 'Нет.',
  '{n} groups': '{n} групп',
  'Based on {n} timestamped posts. Posts marked before timestamps were added aren’t dated.':
    'На основе {n} постов с отметкой времени. Посты, отмеченные до добавления времени, без даты.',

  // ---- Data tools modal ----
  'Data tools': 'Данные',
  'Google Drive backup': 'Резервная копия в Google Drive',
  'Paste a Google OAuth Client ID (Web type). It is public — safe to store here.':
    'Вставьте Google OAuth Client ID (тип Web). Он публичный — хранить здесь безопасно.',
  'Keeps the last 12 timestamped snapshots in your Drive’s hidden app folder.':
    'Хранит последние 12 копий с отметкой времени в скрытой папке приложения в Drive.',
  'Last backup: {date}': 'Последняя копия: {date}',
  'Add the records from a file alongside what you already have (nothing is overwritten). Use the top-bar import button instead to replace everything.':
    'Добавляет записи из файла к уже имеющимся (ничего не перезаписывается). Чтобы заменить всё, используйте кнопку импорта на верхней панели.',
  'Pick records to export. Dependencies are included automatically (a draft brings its groups, a group brings its template, etc.).':
    'Выберите записи для экспорта. Зависимости включаются автоматически (черновик тянет свои группы, группа — свой шаблон и т. д.).',
  'Connect Google Drive': 'Подключить Google Drive',
  'Back up now': 'Сделать копию',
  'Restore…': 'Восстановить…',
  'Disconnect': 'Отключить',
  'Choose a snapshot to restore': 'Выберите копию для восстановления',
  'Use': 'Выбрать',
  'Replace all': 'Заменить всё',
  'Merge / add': 'Объединить / добавить',
  'Backup downloaded. Apply it how?': 'Копия загружена. Как применить?',
  'change Client ID': 'изменить Client ID',
  'Import & merge': 'Импорт и слияние',
  'Choose file to merge…': 'Выбрать файл для слияния…',
  'Export a selection': 'Экспорт выборки',
  'Export {n} selected': 'Экспортировать выбранное ({n})',

  // ---- Global search ----
  'No matches for “{q}”.': 'Нет совпадений по «{q}».',
  'Search drafts, templates, groups… (or drop an image to find it by name)':
    'Поиск по черновикам, шаблонам, группам… (или перетащите изображение, чтобы найти по имени)',
  'Searching by dropped filename. Edit the box to change the query.':
    'Поиск по имени перетащенного файла. Измените текст, чтобы изменить запрос.',
  'Type to search across everything. Press Esc to close, Enter to open the top hit.':
    'Введите запрос для поиска по всему. Esc — закрыть, Enter — открыть первый результат.',

  // ---- Shortcuts ----
  'Keyboard shortcuts': 'Горячие клавиши',
  'Open global search': 'Открыть глобальный поиск',
  'New draft': 'Новый черновик',
  'Switch tabs (Drafts / Templates / Placeholders / Groups / Stats)':
    'Переключение вкладок (Черновики / Шаблоны / Поля / Группы / Статистика)',
  'On Drafts: copy next unposted & open vk.com': 'В Черновиках: скопировать следующий и открыть vk.com',
  'Toggle this help': 'Показать/скрыть эту справку',
  'Close dialogs': 'Закрыть диалоги',
  'Letter shortcuts are ignored while typing in a field.':
    'Буквенные сочетания не работают при вводе в поле.',
};

class I18n {
  locale = $state<Locale>(
    (localStorage.getItem(LS_KEY) as Locale | null) ??
      (navigator.language?.toLowerCase().startsWith('ru') ? 'ru' : 'en'),
  );

  setLocale(l: Locale): void {
    this.locale = l;
    localStorage.setItem(LS_KEY, l);
    document.documentElement.lang = l;
  }
  toggle(): void {
    this.setLocale(this.locale === 'ru' ? 'en' : 'ru');
  }
}

export const i18n = new I18n();
document.documentElement.lang = i18n.locale;

export function t(key: string, params?: Record<string, string | number>): string {
  let s = i18n.locale === 'ru' ? ru[key] ?? key : key;
  if (params) {
    for (const [k, v] of Object.entries(params)) {
      s = s.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v));
    }
  }
  return s;
}
