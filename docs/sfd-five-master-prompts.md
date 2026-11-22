# Parcours SFD : cinq prompts maîtres

Le type de projet **Projet avec spec** conserve son parcours et son référentiel indépendamment du parcours Nodalis 2.0 classique.

## Documents de travail

Les nouveaux projets SFD réutilisent le **fichier `Présentation.md` à la racine du projet** pour les cinq prompts. Aucun deuxième document `SPECS FONCTIONNELLES/presentation projet.md` n'est créé.

```text
Projet avec spec/
├── Présentation.md                         # Document canonique partagé
└── SPECS FONCTIONNELLES/
    ├── PROCESSUS METIERS/
    │   └── PM-0001 - Libellé/
    │       ├── presentation processus.md  # Contenu éditable du dossier PM
    │       └── ACTIVITES/
    │           ├── PM001-ACT001.md        # Une activité = un fichier
    │           └── PM001-ACT002.md
    └── FONCTIONNALITES/
        └── FCT-0001 - Libellé/
            └── presentation fonctionnalité.md
```

Les fichiers ACT ne créent plus de sous-dossiers. La séquence redémarre à `ACT001` pour chaque PM ; les fiches historiques `ACT-0001` et `PM-0001-ACT-001` restent lisibles. Le nom complet du PM historique est préservé dans son dossier et son identité technique.

La création d'une activité nécessite toujours le choix d'un PM parent. Le titre peut être laissé vide ; l'identité stable (GUID) est indépendante du libellé, que l'on peut modifier ensuite. La commande de création ne déclenche aucun prompt ni appel IA.

### Palette, transition et sauvegarde

Depuis **Ctrl+P**, utiliser **Créer un processus métier** ou **Créer une activité**, sans devoir ouvrir le parcours SFD. La palette tente de présélectionner le PM visible dans l'explorateur.

Pour un projet existant contenant des activités-dossiers, la commande **Convertir les anciennes activités SFD en fichiers** présente un aperçu et exige une confirmation. Elle ne convertit que les dossiers de feuilles contenant exclusivement `presentation activité.md`, préserve le texte et le GUID, puis conserve l'ancien dossier dans `.nodalis-sfd-activity-legacy` à la racine (répertoire masqué dans l'arbre). Les dossiers avec sous-dossiers ou autres fichiers ne sont pas touchés ; ils exigent un traitement distinct. Un doublon `presentation projet.md` est archivé uniquement s'il correspond **exactement** au modèle ancien non modifié. Sinon, il est conservé pour éviter toute perte d'information.

Fermer les documents ACT ouverts avant la conversion. Après conversion, les références textuelles qui contenaient l'ancien identifiant ou ancien chemin doivent être vérifiées ; les identifiants techniques GUID sont conservés.

## Ordre des prompts

| Étape | Prompt maître | Cible | Sources préparées |
| --- | --- | --- | --- |
| 1 | Cartographie des processus et activités | Projet | Présentation générale et notes explicitement liées |
| 2 | Détail d'une activité | Une ACT | Présentation du projet, PM parent complet, liste de ses ACT, fiche et ID de l'ACT cible |
| 3 | Assemblage des fonctionnalités | Projet | Présentation du projet, inventaire PM/ACT, présentations PM |
| 4 | Exigences métier | Couple FCT × ACT | FCT entière, toutes ses ACT groupées par PM, ACT cible, exigences candidates et RM |
| 5 | Spécifications transversales | Une FCT | FCT entière, ACT liées, exigences candidates, catalogue RM |

Les instructions des cinq prompts sont embarquées **à l'identique** dans `src/Nodalis.Infrastructure/SfdPrompts/`. Le parcours affiche un aperçu copiable ; il ne lance pas d'IA ni ne valide ou n'importe automatiquement les réponses.

### Depuis le menu Projet → Parcours SFD

1. Renseigner la présentation générale Markdown du projet et lier les notes Markdown/TXT utiles.
2. Initialiser le parcours, ou choisir **Actualiser les 5 prompts** pour un parcours SFD existant.
3. Sélectionner un PM pour créer une activité ou choisir une ACT/FCT pour générer le prompt ciblé.
4. Utiliser les commandes **Déterminer les processus métiers**, **Générer le prompt Activité**, **Assembler les fonctionnalités**, ou **Préparer ce prompt** pour les étapes 04 et 05.
5. Relire les avertissements et les extraits de sources avant de copier un prompt à destination d'une IA externe.

## Interface 1080p

La barre **EXPLORATEUR** et les actions principales tiennent sur une seule ligne. La **loupe** déploie le filtre à la demande ; la refermer efface le filtre actif pour que l'arbre ne reste pas filtré invisiblement. En bas, le menu **Outils** remplace les boutons Calendrier, Kanban, Health check, Couverture, Diagnostic et Corbeille, et contient aussi les options **Application courante** et **Densité Confortable / Compact**. Les raccourcis clavier, préférences enregistrées et commandes initiales restent disponibles.

## Compatibilité et limites

- Un parcours SFD ancien est mis à niveau **après confirmation de l'utilisateur**. Les étapes personnalisées et liens vers sources sont conservés. Les statuts des étapes modifiées reviennent à « À faire ».
- Les anciens contrats JSON `nodalis.sfd.import.v1` et leurs services de comparaison sont préservés pour les historiques ; les **nouveaux prompts maîtres demandent une réponse structurée Markdown** et ne doivent pas être collés dans l'importeur JSON historique.
- Le référentiel actuel conserve encore physiquement certaines exigences EXU/EXS/EXR sous leur activité canonique. Cela ne suffit pas à prouver leur portée exacte **FCT × ACT** ; la préparation des prompts 04 et 05 le signale explicitement. Il faudra adapter la persistance du référentiel pour assurer cette cardinalité de bout en bout.
- Le catalogue partagé des règles métiers reste canonique. L'outil n'invente pas de règles, de couverture, de statut ou de liens.
- Aucune importation ni conversion DOCX n'est réactivée. Les sources autorisées dans cette préparation sont Markdown/TXT déjà présents dans le workspace.

## Qualification

Exécuter depuis Windows avec le SDK .NET requis :

```powershell
dotnet run --project tools/Nodalis.CodeStyle/Nodalis.CodeStyle.csproj --configuration Release -- --check
dotnet build Nodalis.sln --configuration Release
dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release
```

Vérifier le fichier `PM001-ACT002.md`, la numérotation après une ACT historique, la préparation à partir de `Présentation.md`, le renommage sans écrasement, la migration opt-in et ses sauvegardes, ainsi que le retour du filtre et des outils à l'ancienne ergonomie clavier. Qualifier visuellement les commandes et l'explorateur à 1080p.
