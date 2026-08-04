# MemoryKeeper Design System (V1)

> 사진과 추억을 보는 프로그램 — 밝고 편안하며, 오래 봐도 눈이 피로하지 않게.

**기능·DB·ViewModel·Navigation은 변경하지 않는다.**  
화면은 `Mk*` 토큰·스타일만 사용한다.

진입점: `MemoryKeeper.App/Themes/DesignSystem.xaml` → `App.xaml`

---

## Tokens

| 파일 | 내용 |
|---|---|
| `Themes/Tokens/Colors.xaml` | Background Warm Gray · Card White · Primary Blue · Success/Warning/Error |
| `Themes/Tokens/Spacing.xaml` | Card 24 · Section 32 · Component 16 |
| `Themes/Tokens/Radius.xaml` | Card 16 · Image 12 · Button 12 |
| `Themes/Tokens/Typography.xaml` | Title / Section / Body / Caption · Icon sizes |
| `Themes/Tokens/Elevation.xaml` | Border 0 · soft shadow Z |
| `Themes/Tokens/Motion.xaml` | Fade / Hover durations only |

## Styles

| 파일 | 키 |
|---|---|
| `Styles/Text.xaml` | `MkPageTitleStyle`, `MkSectionTitleStyle`, `MkBodyTextStyle`, `MkCaptionTextStyle` … |
| `Styles/Buttons.xaml` | `MkPrimaryButtonStyle`, `MkSecondaryButtonStyle`, `MkDangerButtonStyle`, `MkButtonStyle` (+ hover fade) |
| `Styles/Cards.xaml` | `MkCardStyle`, `MkOutlinedCardStyle`, `MkCompactCardStyle` |
| `Styles/Images.xaml` | `MkImageStyle`, `MkThumbnailBorderStyle`, `MkHeroImageBorderStyle` |
| `Styles/Icons.xaml` | `MkIconStyle` (Segoe Fluent / outlined) |
| `Styles/Empty.xaml` | `MkEmptyStateCardStyle`, title/message |
| `Styles/Inputs.xaml` | TextBox / Search / Combo |

## Color (Light)

| 역할 | 토큰 |
|---|---|
| Window | `MkBrushBackground` `#F3F1ED` |
| Card | `MkBrushSurface` `#FFFFFF` |
| Primary | `MkBrushPrimary` calm blue |
| Success / Warning / Error | Green / Orange / Red |

## Rules

1. Photos before chrome.
2. Prefer shadow over borders.
3. Same card / button / type on Home · 사진첩 · 방문지도 · 여행기록 · 설정.
4. Empty states — never a blank page (`MkEmptyState*`).
5. Motion: hover opacity / fade only.
