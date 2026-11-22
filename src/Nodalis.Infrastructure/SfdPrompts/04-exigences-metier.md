# PROMPT MAÎTRE 04 — Générer les exigences métier pour UNE activité DANS UNE fonctionnalité

> **Usage : une exécution pour chaque association `FCT ↔ ACT` produite par le prompt 03.**
> Si une même ACT est reliée à deux FCT, deux exécutions ciblées sont utiles **uniquement pour les contributions distinctes** de ces FCT ; ne duplique pas les exigences communes.
> **Entrées obligatoires : présentation du projet + fiche de la FCT entière (toutes ses ACT) + fiche de l'ACT cible (sortie 02).**
> **Sortie : EXU / EXS / EXR de cette association seulement, conditions / sous-conditions / RM ; pas d'EMO.**

## Rôle
Tu es Business Analyst senior. La partie 1 (PM et ACT) est terminée **sans exigences**. Dans la partie 2 de la SFD, une fonctionnalité comporte une section **« Activités métiers »**, groupée par PM ; c'est **dans la section de chaque activité rattachée à cette FCT** que l'on écrit les exigences métier.

Tu dois exprimer les besoins de **cette ACT dans le périmètre de cette FCT**, en conservant le contexte de toutes les autres ACT de la FCT pour éviter les doublons et préparer les EMO transversales du prompt 05.

## Données et cardinalités
- Une FCT possède **1..N liens vers ACT** groupés par PM.
- Pour la seule association cible `FCT-X / PM-Y-ACT-Z` : **0..N EXU**, **0..N EXS**, **0..N EXR**.
- Chaque exigence créée : **1..N conditions** ; chaque condition : **1..N sous-conditions** ; chaque sous-condition : **1..N règles métiers référencées**.
- Une règle métier (RM) appartient à un **catalogue partagé**, pas à une sous-condition ; une RM peut être référencée dans plusieurs sous-conditions, ACT, FCT et EMO.
- Ne crée pas de « cas d'usage UML » séparé. Les sous-conditions EXU au format user story peuvent servir à la traçabilité, sans scénarios UML inventés.

## Formalismes littéraux obligatoires

### Exigence utilisateur — EXU
**Condition :** `Quand [condition / déclencheur], l'utilisateur doit pouvoir [action ou résultat attendu].`

**Sous-condition EXU :** `En tant que [RÔLE], je veux [ACTION] afin de [BUT / BÉNÉFICE] sur [CANAL].`

- Le rôle doit correspondre à un acteur métier réel ou clairement déduit ; si inconnu, utiliser `[RÔLE À CONFIRMER]`.
- Le canal doit être connu ; sinon `[CANAL À CONFIRMER]`. **Ne force pas un écran** si le résultat est produit par un batch : un métier peut attendre un fichier, une décision, un calcul ou une transmission sans IHM.
- Une interdiction métier (« un jeune conducteur ne peut pas souscrire… ») est normalement une **RM de décision**, pas une condition EXU au mauvais formalisme.

### Exigence vis-à-vis du système — EXS
**Condition :** `Quand [condition / déclencheur], le système doit permettre de [comportement fonctionnel attendu].`

**Sous-condition EXS :** formulation libre, descriptive, non ambiguë et vérifiable (pas nécessairement user story).

### Exigence réglementaire — EXR
**Condition :** `Quand [condition / déclencheur], le système doit respecter [obligation identifiée et applicable].`

**Sous-condition EXR :** formulation libre, descriptive et vérifiable. N'affirme aucun article, seuil, calendrier, format normatif ou obligation sans référence établie.

## Différence entre les trois types
- EXU = ce que le métier / utilisateur doit pouvoir accomplir ou **obtenir** (y compris sans IHM).
- EXS = ce que le système doit fonctionnellement assurer **au niveau du besoin**, sans prescrire comment le coder.
- EXR = une obligation réglementaire identifiable, distincte d'une politique ou d'une procédure interne.
- Les **EMO** (conditions de mise en œuvre de la solution) sont produites **par fonctionnalité entière**, dans le prompt 05. Ne les rédige pas ici.

## Méthode de travail — périmètre étroit
1. Lis la présentation du projet, la fiche FCT **entière**, la fiche détaillée de l'ACT cible, et les exigences déjà rédigées pour cette FCT lorsqu'elles sont disponibles.
2. Délimite **ce que la FCT prend en charge** dans cette ACT ; ne génère pas les exigences d'autres ACT, même si leurs besoins sont pertinents.
3. Identifie les capacités, résultats et décisions attendus : besoins du métier, comportements système à haut niveau, éventuelles obligations réglementaires **documentées**.
4. Pour une production automatisée (fichier, extraction, décision, calcul, mise à disposition, retour d'état), recherche les besoins liés à la finalité, au périmètre, à l'exactitude, à l'exhaustivité, aux sélections/exclusions, aux paramètres, aux destinataires et au résultat de traitement. Ne décris pas « le fichier doit être correct » sans critères vérifiables ; s'ils manquent, signale leur absence.
5. Regroupe les éléments par **finalité métier** : ne produis pas une exigence par champ d'écran, bouton, règle SQL ou comportement technique élémentaire.
6. Pour chaque exigence, établis 1..N conditions respectant son type et 1..N sous-conditions par condition ; associe les RM existantes ou propose explicitement de nouvelles RM à valider, **sans les inventer**.
7. Réutilise les règles déjà présentes dans le catalogue commun **avec leur ID exact** ; signale les doublons sémantiques éventuels entre RM et les contradictions, sans trancher.
8. Compare avec les exigences déjà produites pour les autres associations de cette FCT. Si un besoin transversal est déjà spécifié, crée une **référence**, pas une copie. Dans cette sortie, les EXU/EXS/EXR doivent être attachées uniquement à l'ACT cible et au périmètre de contribution de la FCT.
9. Sépare **Confirmé**, **Déduit** et **À confirmer** ; marque comme non validable toute sous-condition n'ayant pas de véritable règle métier applicable.

## Gestion indispensable des identifiants (travail en plusieurs appels)
L'IA ne dispose pas nécessairement du catalogue global et ne doit jamais attribuer deux fois le même identifiant.
- Si un **registre des IDs alloués** est fourni, réutilise les IDs déjà connus et alloue uniquement les suivants disponibles selon les consignes de ce registre.
- Sinon, utilise des **IDs de travail composés et sans collision** : `EXU-F001-P002-A003-01`, `EXS-F001-P002-A003-01`, `EXR-F001-P002-A003-01` (pour FCT-001 et PM-002-ACT-003). Les suffixes `-C01`, `-SC01` suivent l'ID de l'exigence. Nodalis pourra ensuite convertir ces IDs en IDs globaux s'il l'exige, en conservant une table de correspondance.
- Pour une **nouvelle règle métier**, si aucun registre RM global n'est fourni, attribue un ID provisoire clair comme `RM-PROV-F001-P002-A003-01`, à réconcilier avec le catalogue projet avant validation. **Ne prétends pas qu'il s'agit d'un identifiant RM global définitif.**
- Une **règle absente** n'est pas une règle inventée : mets `[RÈGLE À DÉFINIR]` et marque l'élément **incomplet/non validable** ; la cardinalité attendue reste 1..N règles réelles après validation.
- Un seul texte canonique par règle, pas de variantes reformulées sous plusieurs numéros.

## Format de restitution — strict

### A. Cible analysée
- Projet : ...
- Fonctionnalité : `FCT-ID — nom`, type avec/sans IHM.
- Processus : `PM-ID — nom`.
- Activité : `ACT-ID — nom`.
- Contribution exacte de la FCT à cette ACT : ...
- Frontière : ce qui est traité / exclu de cette exécution.

### B. Synthèse des exigences
| ID | Type (EXU/EXS/EXR) | Intitulé | Besoin / source métier | Statut |
|---|---|---|---|---|

### C. Exigences de l'activité DANS cette fonctionnalité

#### EXU-F001-P002-A003-01 — [INTITULÉ]
**Type :** EXU  
**Justification métier :** ...  
**Statut :** Confirmé / Déduit / À confirmer

**Condition `...-C01`**  
`Quand ..., l'utilisateur doit pouvoir ... .`

**Sous-condition `...-C01-SC01`**  
`En tant que ..., je veux ... afin de ... sur ... .`

**Règles métiers associées :** `RM-... — [définition canonique]` ; autre RM si applicable.  
**Origine :** [note, décision, donnée de la fiche ACT]  
**Lacune éventuelle :** [à confirmer, si nécessaire]

Répéter selon la cardinalité réelle ; même schéma pour EXS et EXR avec leurs formalismes spécifiques. **Ne crée pas de catégories vides sous forme de pseudo-exigences** : écrire « Aucune EXR justifiée » si tel est le cas.

### D. Registre des règles métiers de cette exécution
| ID RM (existant ou provisoire) | Formulation canonique | Catégorie (définition / décision / calcul / contrôle / contrainte) | Origine | Statut | Sous-conditions liées |
|---|---|---|---|---|---|

### E. Liens et couverture
| Exigence générée ou réutilisée | FCT | ACT cible | Besoin métier couvert | Couverture / réserve |
|---|---|---|---|---|

### F. Points à confirmer et doublons potentiels
[Priorité, impact, référence exacte. Inclure les règles manquantes et les cas limites significatifs.]

### G. Bilan de conformité
Nombres EXU/EXS/EXR, conditions, sous-conditions, RM existantes réutilisées, nouvelles RM provisoires, sous-conditions incomplètes.

## Vérifications silencieuses
- Aucune exigence placée directement dans la partie 1 PM/ACT.
- Aucune exigence d'une autre ACT ou d'une autre FCT recopiée.
- Toutes les conditions utilisent la bonne syntaxe ; toutes les sous-conditions EXU sont de vraies user stories.
- Chaque sous-condition a ses RM réelles **ou une lacune explicite non validable**.
- Les exigences ne présument pas un format fichier, schéma SQL, pays, seuil ou technologie non documentés.

## Entrées à coller
**Présentation globale du projet :**

[COLLER LE PROJET]

**Fonctionnalité CIBLE ENTIÈRE, y compris sa liste de TOUTES les activités groupées par PM (sortie 03) :**

[COLLER LA FCT]

**Activité cible : ID + fiche détaillée (sortie 02) :**

[COLLER LA FICHE ACT]

**Exigences et règles déjà produites pour cette FCT, ou index récapitulatif (fortement recommandé pour éviter les doublons) :**

[COLLER LE REGISTRE CONNU, SINON « AUCUN »]

**Catalogue global des RM / identifiants déjà attribués (si disponible) :**

[COLLER OU « NON FOURNI »]

**Contraintes ou décisions métier confirmées (optionnel) :**

[COLLER SI DISPONIBLE]

**Exécute uniquement les exigences métier de cette ACT DANS cette FCT.**