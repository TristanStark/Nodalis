# Installation de Nodalis

## Systèmes compatibles

- **Windows 10 ou 11, x64**.
- Distribution portable et autonome (self-contained) : **aucune installation séparée de .NET n'est nécessaire**.
- L'application est développée avec **.NET 10 et WPF**. Ces composants intéressent surtout les personnes qui compilent le projet.

## Installer la version stable

1. Ouvrir les [releases de Nodalis](https://github.com/TristanStark/Nodalis/releases).
2. Choisir la release stable souhaitée ; pour cette documentation, `v1.0.0`.
3. Télécharger **Nodalis-win-x64.zip** depuis les artefacts disponibles de la release ou du workflow de publication.
4. Extraire complètement l'archive dans un dossier local, par exemple `C:\Outils\Nodalis`.
5. Lancer `Nodalis.exe`.
6. Lors du premier lancement, choisir ou créer le dossier du **workspace**.

> Le dossier contenant `Nodalis.exe` et celui du workspace doivent être **distincts**. Évitez d'extraire l'application à l'intérieur de votre workspace.

Selon le mode de publication, le ZIP peut être proposé dans les artefacts du workflow GitHub plutôt que dans les assets de la release. Il faut récupérer le package généré pour Windows x64, pas l'archive automatique du code source GitHub.

## SmartScreen

Une application non signée peut afficher une alerte Windows SmartScreen. Vérifiez l'origine du téléchargement. Si un fichier `SHA256SUMS.txt` accompagne le ZIP, vous pouvez contrôler son empreinte depuis PowerShell :

~~~powershell
Get-FileHash .\Nodalis-win-x64.zip -Algorithm SHA256
~~~

Comparez le résultat avec la valeur publiée. N'ignorez pas un avertissement de provenance inconnue.

## Où Nodalis conserve-t-il ses fichiers ?

- **Documents, données métier et pièces jointes** : dans le workspace choisi.
- **Préférences du poste** : `%LOCALAPPDATA%\Nodalis\preferences.json`.
- **Diagnostics** : `%LOCALAPPDATA%\Nodalis\Diagnostics`.

Les préférences ne sont pas les documents du workspace et peuvent être gérées séparément.

## Et ensuite ?

Continuez avec [[Premiers-pas]]. Pour remplacer une version existante, utilisez plutôt [[Mise-a-jour-et-compatibilite]] afin de connaître les contrôles de compatibilité et le rollback.

**Référence :** [guide utilisateur du dépôt](https://github.com/TristanStark/Nodalis/blob/develop/docs/README-UTILISATEUR.md).
