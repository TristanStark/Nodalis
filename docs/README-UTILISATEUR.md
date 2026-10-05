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

Nodalis peut installer une release **depuis une archive locale**, sans téléchargement réseau depuis l'application.

1. Récupérez `Nodalis-win-x64.zip` par le canal de votre choix.
2. Dans Nodalis, cliquez sur **Mise à jour**.
3. Sélectionnez l'archive ZIP.
4. Nodalis vérifie avant fermeture :
   - le produit et la version ;
   - la cible Windows x64 ;
   - la compatibilité du schéma de workspace ;
   - la présence de tous les fichiers ;
   - la taille et le SHA-256 de chaque fichier déclaré dans `release-manifest.json`.
5. Confirmez l'installation. Les documents ouverts sont enregistrés, Nodalis se ferme, `Nodalis.Updater.exe` remplace les binaires puis relance l'application.

La version remplacée est conservée dans un dossier frère `.previous`. Le bouton **Rollback** permet de revenir à cette version. Après un rollback, la version quittée devient à son tour la version disponible pour un nouveau rollback.

Le workspace n'est jamais utilisé comme zone de staging ou de sauvegarde de version. Si le dossier de l'application et le workspace se chevauchent, Nodalis refuse la mise à jour et le rollback.

Vous pouvez toujours conserver une copie supplémentaire du workspace avec la fonction **Sauvegardes** avant une mise à jour importante.

## Sauvegarder Nodalis

Pour sauvegarder les données métier, copiez simplement le dossier complet du workspace.

Les données restent lisibles sans Nodalis :
- contenu principal en Markdown ;
- métadonnées en JSON ;
- pièces jointes et sources importées sous forme de fichiers ordinaires.

## Rechercher dans le workspace

La recherche est **fuzzy par défaut** : elle accepte les fautes courantes et
les mots incomplets. Activez **Mode exact** pour revenir à une recherche
littérale insensible à la casse.

Les résultats sont classés de façon déterministe : le titre du fichier est
prioritaire sur les headings Markdown, eux-mêmes prioritaires sur le corps du
texte. À qualité égale, le projet courant est favorisé devant l'application
courante, puis le reste du workspace.

Des filtres peuvent être combinés au texte recherché :

- `type:specification` pour le type défini dans le front matter ;
- `date:2026-10-05` pour la date de modification UTC du fichier ;
- `project:"Projet Patate"` pour limiter à un projet ;
- `status:active`, `owner:alice`, `tag:api` pour les propriétés standard ;
- `@reviewer:alice` pour une propriété personnalisée.

Les valeurs contenant des espaces peuvent être placées entre guillemets.
La recherche reste entièrement locale et ne nécessite ni serveur d'index ni
base de données opaque.

## Import Word

L'import DOCX travaille sur une copie temporaire et affiche son plan d'écriture avant validation. Le fichier Word original n'est jamais modifié.

## Réseau et confidentialité

Nodalis ne contient aucun appel réseau applicatif. Les données restent locales à la machine et aux emplacements que vous choisissez.

## Windows SmartScreen

Une archive ou un exécutable non signé peut déclencher un avertissement SmartScreen lors du premier lancement. Vérifiez que le fichier provient bien de la release GitHub officielle du projet et, si disponible, comparez son SHA-256 avec le fichier `SHA256SUMS.txt`.
