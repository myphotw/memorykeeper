import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = [
    ROOT / "Views" / "GalleryPage.xaml",
    ROOT / "Views" / "FavoritesPage.xaml",
    ROOT / "Views" / "TravelRecordsPage.xaml",
    ROOT / "Views" / "HomePage.xaml",
]


def convert(text: str) -> str:
    text = re.sub(r'\s+x:DataType="[^"]+"', "", text)
    text = text.replace('Tag="{x:Bind}"', 'Tag="{Binding}"')
    text = re.sub(r"\{x:Bind\s+ViewModel\.([^}]+)\}", r"{Binding \1}", text)
    text = re.sub(r"\{x:Bind\s+([^}]+)\}", r"{Binding \1}", text)
    return text


def main() -> None:
    for path in FILES:
        original = path.read_text(encoding="utf-8")
        updated = convert(original)
        path.write_text(updated, encoding="utf-8")
        print(f"{path.name}: x:Bind left={updated.count('x:Bind')} changed={original != updated}")


if __name__ == "__main__":
    main()
