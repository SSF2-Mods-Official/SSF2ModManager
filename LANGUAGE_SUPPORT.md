# Adding Language Support to SSF2 Mod Manager

This document outlines strategies for adding localization (language) support to the project, with a focus on making it easy for contributors to add new languages.

## Approaches

### 1. Resource Files (.resx)
- Store all UI strings in .resx files (e.g., `Strings.en.resx`, `Strings.fr.resx`).
- Each language has its own .resx file.
- Contributors copy the English file, translate the values, and rename it (e.g., `Strings.es.resx`).
- WPF can bind to these resources using `{x:Static}` or a custom markup extension.
- **Pros:** Standard for .NET, integrates with Visual Studio.
- **Cons:** Less approachable for non-developers.

### 2. XAML Resource Dictionaries
- Store all translatable strings in XAML `ResourceDictionary` files (e.g., `Strings.en.xaml`, `Strings.de.xaml`).
- Load the appropriate dictionary at runtime based on user selection.
- Contributors add a new XAML file for their language.
- **Pros:** Easy to edit, works well with WPF binding.
- **Cons:** Slightly more setup for dynamic switching.

### 3. JSON or XML Language Packs
- Store translations in JSON or XML files (e.g., `lang-en.json`, `lang-ru.json`).
- At startup, load the selected language file into a dictionary.
- Contributors add a new JSON/XML file for their language.
- **Pros:** Very approachable for non-developers, easy to diff and edit.
- **Cons:** Requires custom code to bind strings in XAML.

## Language Selection
- Add a settings option for users to select their language.
- Detect system language by default, but allow override.

## Community Contribution
- Document the process: “Copy the English file, translate the values, and submit a pull request.”
- Optionally, provide a script or tool to validate missing translations.

## Recommendation
- For most WPF projects, .resx is standard, but JSON or XAML is easier for non-developers.
- Choose the method that best fits your contributor base.

## Example Workflow (JSON):
1. Copy `lang-en.json` to `lang-xx.json` (where `xx` is the language code).
2. Translate the values.
3. Add the file to the `Languages/` folder.
4. Submit a pull request.

---

For implementation samples or more details, see the project wiki or ask for a code example.