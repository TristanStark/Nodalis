# Sources et publication du Wiki GitHub

## Pourquoi ce répertoire existe

`docs/wiki/` contient les pages Markdown **prêtes à être publiées dans le Wiki GitHub**. Ce répertoire est versionné avec le code sur `develop`, ce qui permet de relire les modifications documentaires dans les commits/PR.

**Important :** committer un fichier dans `docs/wiki/` ne publie pas automatiquement le contenu dans l'onglet *Wiki*. Le Wiki GitHub possède son propre dépôt Git, `TristanStark/Nodalis.wiki.git`.

## Publication manuelle sous Windows

1. Dans GitHub, ouvrir l'onglet [Wiki](https://github.com/TristanStark/Nodalis/wiki). S'il n'existe pas encore de page, créer une première page `Home` depuis l'interface Web pour initialiser le dépôt Wiki.
2. Avoir `git` installé sur le poste et une authentification GitHub configurée pour pouvoir pousser sur le Wiki.
3. Depuis la racine du dépôt Nodalis, exécuter :

~~~powershell
.\scripts\publish-wiki.ps1 -Preview
.\scripts\publish-wiki.ps1
~~~

L'option `-Preview` clone le Wiki dans un répertoire temporaire et montre les changements sans commit ni push.

Le script publie uniquement les fichiers `*.md` présents dans `docs/wiki/`. Il conserve les pages supplémentaires déjà présentes dans le Wiki ; il met à jour les pages portant le même nom.

## Vérifications

- Vérifier que `Home` est la page d'accueil.
- Vérifier que `_Sidebar.md` permet de naviguer entre les pages.
- Tester quelques liens `[[...]]`.
- Contrôler que la roadmap ne présente pas la 1.1–1.4 comme livrée.

## Maintenance

Modifier la **source** dans `docs/wiki/` puis republier avec le script. Éviter d'entretenir indépendamment deux versions divergentes du Wiki. Les documents détaillés dans `docs/` constituent la référence technique du produit.
