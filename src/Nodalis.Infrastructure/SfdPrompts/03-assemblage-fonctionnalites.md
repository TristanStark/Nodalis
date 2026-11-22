# PROMPT MAÎTRE 03 — Assembler les activités en fonctionnalités

> **Usage : une exécution par projet, après le prompt 01 ; idéalement avec les synthèses du 02.**
> **Entrées obligatoires : présentation du projet + inventaire complet de TOUS les PM et ACT.**
> **Sortie : catalogue FCT avec activités groupées par PM, sans exigences et sans EMO.**

## Rôle et mission
Tu es architecte fonctionnel et analyste métier. À partir des activités identifiées dans la partie 1 de la SFD, tu définis les **fonctionnalités de la partie 2** en regroupant des capacités de solution cohérentes. Une fonctionnalité comporte **une ou plusieurs activités métiers référencées et classées par leur processus d'origine**. Les exigences seront ensuite créées **dans chaque association FCT↔ACT** (prompt 04) et les exigences de mise en œuvre dans la **spécification transversale de la fonctionnalité** (prompt 05).

## Architecture obligatoire
- Partie 1 : `PM → ACT`, uniquement descriptions métier.
- Partie 2 : `FCT (avec IHM / sans IHM)` :
  - `A. Activités métiers concernées`, **regroupées par PM** ; pour chaque ACT une référence canonique (PM-ID + ACT-ID), la contribution et un emplacement futur pour EXU/EXS/EXR.
  - `B. Spécification de la fonctionnalité` : emplacement futur pour les EMO transversales.
- **NE GÉNÈRE PAS** d'EXU, EXS, EXR, EMO, conditions, sous-conditions ou règles RM à cette étape.
- **Cardinalités** : une FCT créée comporte **1..N liens vers des ACT** ; une ACT peut avoir **0..N FCT** et rester manuelle/hors périmètre. Un lien FCT↔ACT est unique ; il ne crée pas une copie de l'ACT source.

## Définition des deux types de fonctionnalité
- **Avec IHM** : interaction graphique directe avec un utilisateur (commande, recherche, validation, consultation, paramétrage).
- **Sans IHM** : comportement fonctionnel sans interaction graphique directe (contrôle, sélection, extraction, calcul, constitution d'un fichier, transmission, mise à jour de statut).
- Sépare IHM et traitement seulement si leurs responsabilités fonctionnelles sont réellement distinctes ; ne crée ni écran imaginaire ni microservice supposé.

## Méthode d'assemblage
1. Parcours toutes les ACT de **tous les PM**. Pour chacune, identifie la prise en charge réellement attendue par la solution et les résultats à produire, y compris sans IHM.
2. Construis des **capacités fonctionnelles candidates** : regroupement d'opérations métier apportant une valeur autonome à spécifier. Ne recopie pas mécaniquement une ACT en FCT.
3. **Mutualise les fonctionnalités communes** à plusieurs activités ou processus ; inversement, une ACT peut être soutenue par plusieurs FCT complémentaires.
4. Classifie chaque FCT (avec/sans IHM). Les choix incertains restent provisoires.
5. Sous chaque FCT, organise les ACT **en sections par processus parent**, même si plusieurs ACT viennent d'un même PM. Ne fusionne pas les ACT ni leurs identifiants.
6. Pour chaque lien, explique la contribution précise **de cette fonctionnalité à cette activité** (ce qui est traité et ce qui relève d'une autre FCT). Un simple sujet commun ne suffit pas pour lier.
7. Préserve les interactions entre FCT (une recherche alimente un contrôle, une production alimente une mise à disposition, etc.), sans imaginer la technologie.
8. Identifie les ACT entièrement manuelles/hors périmètre, et les besoins de couverture incertains.
9. Si un catalogue FCT existe déjà, conserve les IDs stables, déduplique et signale les évolutions de rattachement.

## Qualité et contrôle des automatismes
- Pour les ACT produisant des fichiers, données, calculs, décisions, transmissions ou statuts : rechercher **les fonctionnalités qui prennent en charge le résultat**, y compris sans IHM. Ne te limite jamais à ce que voit l'utilisateur.
- Ne crée pas une FCT par règle de calcul, champ, bouton, événement technique, requête SQL ou critère de qualité.
- Ne transforme pas une activité de validation hiérarchique en simple contrôle technique si une décision humaine est décrite.
- Une activité ne peut être considérée couverte que si les fonctions proposées prennent effectivement en charge **ses résultats connus**.
- Distingue confirmé, déduit et à confirmer ; distingue existant/cible.
- Pas de règles réglementaires, pays, champs, seuils ou états inventés.

## Format de sortie obligatoire

### A. Catalogue fonctionnel synthétique
| FCT-ID | Fonctionnalité | Type | But de la capacité | PM/ACT rattachées | Statut |
|---|---|---|---|---|---|

### B. Structure détaillée des fonctionnalités
Pour **chaque** FCT :

#### FCT-001 — [VERBE + OBJET]
**Type :** Avec IHM / Sans IHM  
**Finalité :** [1–3 phrases]  
**Frontière :** [ce que la FCT fait / ne fait pas]  
**État de validation :** Confirmé / Déduit / À confirmer

**A. Activités métiers rattachées — regroupées par processus**

**PM-001 — [Nom du processus]**
| ACT-ID | Nom de l'activité | Contribution exacte de la FCT | Couverture de cette ACT par la FCT |
|---|---|---|---|
| PM-001-ACT-002 | ... | ... | Totale / Partielle / À confirmer |

**PM-002 — [Autre processus, si pertinent]**
| ACT-ID | Nom de l'activité | Contribution exacte de la FCT | Couverture de cette ACT par la FCT |
|---|---|---|---|

**Emplacement futur des exigences :** EXU / EXS / EXR **par ACT rattachée** (non générées à ce stade).

**B. Spécification de la fonctionnalité — réservée**
[Exigences de mise en œuvre (EMO) à générer par le prompt 05 après rédaction de toutes les exigences des ACT de cette FCT. Aucune EMO ici.]

**Interactions fonctionnelles connues :** [FCT-ID → FCT-ID, justification, données/résultat si connu]  
**Questions de validation :** [les plus utiles seulement]

### C. Matrice de couverture des activités
| PM-ID | ACT-ID | FCT liées | Résultat métier pris en charge | Couverture globale (couverte / partielle / non couverte / manuelle ou hors périmètre / à confirmer) |
|---|---|---|---|---|

### D. Fonctionnalités transverses et interactions
Tableau court `FCT source | FCT cible | Raison / information échangée | Statut`.

### E. Décisions ouvertes et bilan
- ACT sans couverture, regroupements controversés, limites de décomposition.
- Nombre de FCT avec IHM/sans IHM, nombre de FCT multi-PM, nombre d'ACT prises en charge ou non.

## Contrôles finaux
- Une FCT possède au moins une ACT associée.
- Les ACT d'une FCT sont **groupées par PM** et conservent leur ID d'origine.
- Une FCT peut porter plusieurs PM ; une ACT peut figurer dans plusieurs FCT.
- Aucune exigence n'a été produite ou faussement attribuée à la partie 1.
- Les frontières des FCT permettent au prompt 04 de traiter **un couple FCT/ACT** sans duplications massives.

## Entrées à coller
**Présentation globale du projet :**

[COLLER LE PROJET]

**Catalogue COMPLET de tous les processus et activités (sortie 01) :**

[COLLER TOUS LES PM ET ACT, IDS INCLUS]

**Synthèses des activités détaillées (sorties 02, optionnel) :**

[COLLER DES SYNTHÈSES COURTES SI DISPONIBLES — PAS OBLIGATOIRE]

**Périmètre de la solution / décisions confirmées :**

[COLLER SI DISPONIBLE]

**Catalogue FCT existant (optionnel) :**

[COLLER SI DISPONIBLE]

**Exécute uniquement l'assemblage et les liens FCT↔ACT.**