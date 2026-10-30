# DropMe

Étagère flottante pour Windows : glissez un fichier, secouez la souris, une petite fenêtre
apparaît pour le déposer le temps de changer de dossier ou d'application.

## Fonctionnement

- **Secouer la souris** pendant un glisser (clic gauche enfoncé) ouvre une étagère près du curseur.
- **Déposer** fichiers, dossiers, images de sites web, textes ou liens dessus.
- **Glisser la pile** vers un dossier ou une app pour tout déplacer ; **cliquer** dessus la déplie
  pour voir chaque fichier et les glisser un par un.
- **Clic sur l'icône** de la zone de notification : fenêtre DropMe avec les étagères ouvertes et
  les dernières fermées, à rouvrir d'un clic. Le nombre d'étagères gardées (3 à 50, 5 par défaut)
  se règle avec ⚙ ou dans le menu de l'icône.

Les étagères ne gardent que des chemins vers les fichiers. Les images, textes et liens venus
d'autres applications sont enregistrés dans `%TEMP%\DropMe` ; l'historique et les réglages dans
`%APPDATA%\DropMe`.

## Développement

Prérequis : SDK .NET 8.

```
dotnet build -c Release
```

## Installeur

Prérequis : SDK .NET 8 et Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Produit `dist\DropMe-Setup-<version>.exe` (autonome, sans droits administrateur). La version vient
de `<Version>` dans `DropMe.csproj`.
