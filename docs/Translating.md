# Translations

MasterSafe Authenticator is available in **English**, **Spanish (Español)** and **Finnish (Suomi)**. The language follows the
computer by default; change it in Settings, Language. A change applies completely the next time the program starts.

## How it works

The English text in the program is the key. Wherever a label, button, hint, tooltip, window title or message is set, the program
looks for a line with exactly that English text and shows its translation; with no line, the English text stays. So a missing
translation is never an error, only untranslated text.

- `src/MasterSafe.App/Languages/<code>.txt`: one line per text, `English text => translation`.
  - `{0}`, `{1}` stand for changing parts in both sides, for example `Deleted {0}. => {0} poistettu.`
  - Keep access-key underscores (`_Settings => _Asetukset`).
  - A line starting with `#` is a comment.
- `src/MasterSafe.App/Languages/<code>.help.txt`: the Help topics. Each starts with `### <English title>`, then `title:`,
  `summary:` and `body:` (the text up to the next `###`).

## Adding a language

1. Copy `es.txt` and `es.help.txt` to the new two-letter code and translate the right-hand sides.
2. Add the code to `Loc.Choices`, `Loc.NameOf` and `Loc.Resolve` in `Services/Loc.cs`.
3. Run the tests. `LocalizationTests` fails when a text on any screen or a Help topic has no translation, and when the
   languages translate different sets of texts.

## When you add a text to a screen

Add the same English text to every language file. The test above reminds you. Text built from several XAML pieces is not translated
as a whole; write it as one string so it can be.
