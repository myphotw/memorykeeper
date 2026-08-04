import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = [
    ROOT / "Views" / "VisitRecordPage.xaml",
    ROOT / "Views" / "PendingMemoryPage.xaml",
    ROOT / "Views" / "ImportPage.xaml",
    ROOT / "Views" / "PlaceManagementPage.xaml",
    ROOT / "Views" / "SettingsPage.xaml",
    ROOT / "Views" / "PhotoDetailPage.xaml",
    ROOT / "Views" / "TravelRecordsDetailPage.xaml",
    ROOT / "Views" / "TagManagementPage.xaml",
    ROOT / "Views" / "StorageManagementPage.xaml",
    ROOT / "Views" / "SetupWizardPage.xaml",
]


def convert(text: str) -> str:
    text = re.sub(r'\s+x:DataType="[^"]+"', "", text)
    text = text.replace('Tag="{x:Bind}"', 'Tag="{Binding}"')
    text = re.sub(r"\{x:Bind\s+ViewModel\.([^}]+)\}", r"{Binding \1}", text)
    text = re.sub(r"\{x:Bind\s+([^}]+)\}", r"{Binding \1}", text)
    return text


def main() -> None:
    for path in FILES:
        if not path.exists():
            print(f"missing {path.name}")
            continue
        original = path.read_text(encoding="utf-8")
        updated = convert(original)
        path.write_text(updated, encoding="utf-8")
        print(f"{path.name}: x:Bind left={updated.count('x:Bind')} changed={original != updated}")


if __name__ == "__main__":
    main()
