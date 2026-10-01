# Графические обозначения продуктов

Текущие значки созданы с помощью Imagegen для AI Usage Monitor и выбраны пользователем:

- Codex — белое квадратное переплетение четырёх лент.
- Claude — персиковый лучистый символ с восемью округлыми лучами и круглым центром.

Они используются для различения продуктов внутри приложения. Это не официальные
логотипы OpenAI или Anthropic; приложение не связано с этими компаниями и не одобрено ими.
Названия продуктов принадлежат их владельцам. Создание новых изображений само по себе
не является юридической гарантией отсутствия сходства с товарными знаками.

Исходные PNG с прозрачным фоном находятся в `src/AIUsageMonitor/Assets/provider_codex.png`
и `provider_claude.png`; Android использует копии этих же файлов.

## Подготовка ресурсов

Использован встроенный Imagegen. Выбранные символы выделены из одобренных эскизов
в отдельные PNG с прозрачным фоном. Финальные инструкции:

<details>
<summary>Промпты подготовки значков</summary>

**Codex**

```text
Edit this reference into ONE final standalone transparent icon asset. Extract ONLY the TOP LEFT white square interwoven symbol selected by the user. Preserve its exact fourfold square silhouette, the four rounded angular interwoven bands, orientation and central square hole. Remove the dark background and all three other symbols entirely. Output a square canvas with genuine alpha transparency, including all holes and narrow over-under separation channels. Center the selected symbol occupying approximately 90 percent of the canvas with equal small margins. Make the symbol uniformly solid off-white #EEF5FA, clean crisp flat edges, no gradients, texture, shadows, background, border, letters or other elements. Preserve the chosen design rather than inventing a new one. This is the final tiny UI asset.
```

**Claude — финальная очистка выбранного лучистого символа**

```text
Clean up this icon precisely. Preserve the peach center circle and all EIGHT peach rounded rays exactly in position, shape and size. Delete EVERY stray red, yellow and white speckle between and outside the rays. Delete edge halo and random dots. All space other than the eight rays and center disk must be fully transparent alpha=0. The only visible color must be uniform peach #EAB894. The disk and rays must have perfectly smooth antialiased boundaries and uniform solid fill, no gradient, texture, colored fringe, glow or outline. Keep square canvas, composition, center and selected design unchanged. A spotless simple flat UI glyph on true transparent background. Output the clean icon, not a mockup.
```

</details>

## Исторические изображения

В предыдущих версиях логотипы были получены из [Lobe Icons](https://github.com/lobehub/lobe-icons/tree/master/packages/static-svg/icons).
Текст лицензии ниже сохранён для этих исторических изображений. Текущие значки
не используют файлы Lobe Icons.

Lobe Icons распространяется по лицензии MIT:

```text
MIT License

Copyright (c) 2023 LobeHub

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
