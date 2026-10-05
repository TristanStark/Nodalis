# Nodalis — guide utilisateur portable

## Démarrer

1. Téléchargez l'archive `Nodalis-win-x64.zip`.
2. Extrayez **tout le contenu** dans un dossier local, par exemple `C:\Outils\Nodalis`.
3. Lancez `Nodalis.exe`.
4. Au premier démarrage, choisissez le dossier qui contiendra votre workspace Nodalis.

Nodalis est publié pour **Windows 10/11 x64** en mode **self-contained** : aucun runtime .NET, Visual Studio ou installateur n'est requis.

## Où sont stockées les données ?

Le dossier de l'application et le workspace sont indépendants.

- Le **workspace** contient les fichiers Markdown, JSON, pièces jointes et imports.
- Les **préférences locales** sont stockées dans `%LOCALAPPDATA%\Nodalis\preferences.json`.
- L'exécutable Nodalis ne contient pas le workspace.

Pour simplifier les mises à jour, placez de préférence le workspace dans un dossier distinct de celui de l'application.

## Mettre Nodalis à jour

1. Fermez Nodalis.
2. Sauvegardez votre workspace si vous souhaitez une copie de sécurité supplémentaire.
3. Téléchargez la nouvelle archive.
4. Extrayez-la dans un nouveau dossier ou remplacez l'ancien dossier de l'application.
5. Relancez `Nodalis.exe`.

Le workspace et les préférences locales ne sont pas supprimés par le remplacement du dossier de l'application.

**Ne supprimez pas votre dossier de workspace pendant une mise à jour.**

## Sauvegarder Nodalis

Pour sauvegarder les données métier, copiez simplement le dossier complet du workspace.

Les données restent lisibles sans Nodalis :
- contenu principal en Markdown ;
- métadonnées en JSON ;
- pièces jointes et sources importées sous forme de fichiers ordinaires.

## Import Word

L'import DOCX travaille sur une copie temporaire et affiche son plan d'écriture avant validation. Le fichier Word original n'est jamais modifié.

## Réseau et confidentialité

Nodalis ne contient aucun appel réseau applicatif. Les données restent locales à la machine et aux emplacements que vous choisissez.

## Windows SmartScreen

Une archive ou un exécutable non signé peut déclencher un avertissement SmartScreen lors du premier lancement. Vérifiez que le fichier provient bien de la release GitHub officielle du projet et, si disponible, comparez son SHA-256 avec le fichier `SHA256SUMS.txt`.
