# Parcours SFD : cinq prompts maîtres

Le type de projet **Projet avec spec** conserve son parcours et son référentiel indépendamment du parcours Nodalis 2.0 classique.

## Documents de travail

Les projets SFD neufs disposent de :

```text
SPECS FONCTIONNELLES/
├── presentation projet.md                 # Contexte éditable à renseigner
├── PROCESSUS METIERS/
│   └── PM-0001 - Libellé/
│       ├── presentation processus.md      # Présentation du PM dans le dossier
│       └── ACTIVITES/
│           ├── PM-0001-ACT-001/
│           │   └── presentation activité.md
│           └── PM-0001-ACT-002/
│               └── presentation activité.md
└── FONCTIONNALITES/
    └── FCT-0001 - Libellé/
        └── presentation fonctionnalité.md
```

Les anciens projets sont conservés tels quels, sans création implicite ni renumérotation. Les anciens identifiants `ACT-0001` restent lisibles. Les nouveaux codes sont séquentiels **à l'intérieur d'un processus** et laissent les autres processus indépendants.

La création d'un PM crée un dossier avec une présentation Markdown rédigée par l'utilisateur. La création d'une ACT nécessite un PM existant ; elle produit une fiche canonique et attribue automatiquement le prochain numéro disponible de ce PM. Son titre est facultatif lors de la création et peut être modifié ensuite sans toucher à son GUID ni à son code.

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

Vérifier en particulier l'auto-incrémentation de trois ACT dans un PM, le redémarrage à 001 dans un deuxième PM, la préservation des textes lors du renommage et le contenu effectivement copié pour chacun des cinq prompts.
