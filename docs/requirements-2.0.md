# Exigences — Nodalis 2.0 (premier incrément)

Les exigences sont des **fichiers Markdown indépendants** dans
`Documentation/Exigences/EXI-0001.md`, à l'intérieur d'un projet.
Chaque exigence possède un GUID persistant distinct de sa référence lisible.
La lecture ne crée pas de fichiers ni ne migre les anciens documents.

## État initial et sémantique

Le format portable `nodalis-requirement-schema: 1` utilise un front matter
et quatre champs d'identité (GUID de l'exigence, GUID du projet, référence
EXI-xxxx, titre) ainsi que la date de création. Le titre est encodé comme
une chaîne JSON pour éviter l'injection de champs YAML.

Les quatre dimensions sont distinctes :
- `kind` : classification, initialement `Unclassified` ;
- `validation` : décision métier, initialement `Draft` ;
- `implementation` : réalisation, initialement `NotStarted` ;
- `verification` : preuve fonctionnelle, initialement `NotVerified`.

**Aucune présence de lien, tâche, test ou fichier ne modifie ces états**.
La création n'est ni une approbation, ni une réalisation, ni une vérification.
Les états `Approved` et `Verified` ne sont pas proposés par ce premier
incrément : les transitions et les décisions humaines historisées
arriveront avec les incréments suivants.

## Création et sécurité

Le service `ProjectRequirementRegistryService` fournit `ListAsync`
(lecture seule) et `CreateAsync` (création explicite).

- Les dossiers ne sont créés que lors d'une création approuvée.
- Un projet archivé ou hors workspace ne peut pas recevoir d'exigences.
- Aucune réécriture automatique d'un ancien document.
- Le prochain code est proposé en lecture ; l'interface pourra figer
  un `expectedReference` entre prévisualisation et validation.
- Publication atomique d'un nouveau fichier, sans écraser un homonyme.
- Rejet des dossiers liés/jonctions, sources non UTF-8, GUID/références
  ambigus, cartes hors projet, front matter invalide ou incompatible.
- Le registre est recalculé depuis les sources Markdown, pas stocké
  dans une deuxième base ni reconstruit à chaque dessin WPF.

La section « Critères d'acceptation » reste vide à la création.
Un critère réel devra recevoir un GUID de bloc stable via la commande
existante « Identifier le titre actuel » ; aucun critère fictif n'est
comptabilisé comme couvert.

## Qualification actuelle

Tests synthétiques : projet principal et autre projet, ancien fichier
réservant une référence, aller-retour format/parse, doublons de GUID,
collisions de références, mauvais propriétaire, annulation, état inconnu,
injection de champs YAML et confirmation de l'absence de mutation.

La Command Palette propose **Créer une exigence**. La commande demande
un titre, affiche le nom du projet, la référence et le chemin prévus,
puis exige une confirmation avant écriture. Si le registre évolue entre
l'aperçu et la validation, l'action échoue plutôt que d'attribuer une
nouvelle référence en silence. La fiche créée s'ouvre dans l'éditeur.

**À venir (EPIC #198)** : critères `CA-xx` identifiés, qualification humaine avec journal
append-only, liens critères→tests et matrice de traçabilité distinguant
couverture, exécution, conformité et décision.

Aucun modèle d'IA ni accès réseau requis.

La lecture du registre et la création sont exécutées sur un worker :
l'énumération de fichiers et le parsing du Markdown ne bloquent pas
le Dispatcher WPF. Si l'utilisateur navigue ailleurs pendant la création,
le nouveau document reste enregistré au projet approuvé sans détourner
le focus vers un autre projet.

## Critères d'acceptation — projection en lecture seule

`ProjectRequirementCriterionCodec.Inspect` examine les titres de niveau 3
situés dans `## Critères d'acceptation` (pas ailleurs) :
`### CA-01 — Résultat attendu` suivi immédiatement de
`<!-- nodalis:block:GUID -->`. Les titres dans des blocs de code
Markdown sont ignorés. Les marqueurs utilisent le codec de blocs
Nodalis 2.0 existant ; le texte d'un critère peut être renommé sans
perdre son GUID.

Un critère sans identifiant, sans libellé ou avec un numéro local
répété est signalé et **n'est pas identifié localement sans ambiguïté**.
Les collisions de GUID entre fichiers doivent encore être vérifiées
par l'index partagé avant toute liaison à un test. La présence d'un
critère ne signifie pas qu'il est couvert, exécuté ou conforme.

La projection `ProjectRequirementEntry.Criteria` est dérivée du
Markdown lors de la lecture, sans écrire de registre parallèle.
Les diagnostics de critères sont séparés des diagnostics de fichiers
invalides. Les smoke tests couvrent les doublons, titres renommés,
sections externes, marqueurs malformés et blocs de code.

## Traçabilité calculée — liens de test (02/11/2026)

La projection `ProjectRequirementTraceability.Build` joint le registre
canonique des exigences et **un instantané actualisé** de l'index de liens
partagé (v5) : aucune nouvelle base, aucune écriture, aucun scan pendant
le rendu WPF. Les critères ont quatre états **de couverture uniquement** :
`Unidentified`, `StaleSource`, `Uncovered`, `LinkedTest`.

Un critère est `LinkedTest` uniquement si son GUID et son document sont
uniques, si l'empreinte de la fiche concorde avec l'index, et si une
relation typée `vérifie` ou `teste` est issue d'un bloc de test identifié
et d'une source **du même projet**. Les blocs de test sont reconnus dans
les dossiers `Tests`/`Recette` ou par titre `TEST-`/`TST-` ; ce filtre
provisoire sera remplacé par le référentiel de tests de l'EPIC #200.
Les relations dupliquées vers un même bloc de test sont dédoublonnées.
Les sources documentaires sans bloc, hors projet, non typées, en
collision ou issues d'un index désynchronisé ne créent pas de couverture.

**Important :** `LinkedTest` signifie seulement « au moins un test est
lié ». Ce n'est **pas** « test exécuté », « résultat conforme »,
« preuve vérifiée », « exigence approuvée » ou « vérification acquise ».
La matrice n'altère jamais les trois états de la fiche. Les campagnes,
passages et preuves seront joints après leur implémentation (#200).
Les tests synthétiques couvrent les sources de l'autre projet, le GUID
reproduit, les SHA obsolètes, la ligne déplacée et les liens redondants.

## Consultation dans l'application (02/11/2026)

La palette propose désormais **Matrice de traçabilité des exigences**.
Le panneau charge le registre d'exigences et rafraîchit l'index partagé
sur un worker, puis joint les sources en mémoire. La recherche, les
filtres de couverture et la sélection ne provoquent aucun accès disque.
Les critères sont virtualisés, avec détails et diagnostics de source.
La navigation vers une fiche EXI réutilise l'ouverture documentaire sûre ;
la navigation vers un bloc de test utilise son GUID et revérifie son
identité dans le document au moment du clic. Les documents source ne
sont pas modifiés et aucun statut n'est mis à jour.

Le détail affiche explicitement que l'exécution et la conformité ne
sont pas connues tant que les campagnes de tests (#200) ne sont pas
intégrées. La qualification visuelle en WPF/dark mode reste nécessaire.

## Historique de tests associé aux critères (03/11/2026)

La projection en lecture seule `ProjectRequirementCampaignTraceability.Build`
associe la matrice EXI/CA/test existante aux campagnes REC **déjà vérifiées
pour le projet sélectionné**. La jointure conserve les GUID stables et
réexamine l'appartenance exacte du fichier REC au même projet. Les
sources figées provenant d'un autre projet, les campagnes d'un projet
voisin et les identifiants de campagne dupliqués ne contribuent pas.

Chaque association conserve un **passage particulier**, avec sa campagne,
sa date, son environnement, sa version, le contenu de test figé (SHA),
le verdict explicitement déclaré par l'exécutant ou l'absence de verdict,
le motif et la référence de preuve **non vérifiée**. Les passages ne sont
pas fusionnés : une réussite historique dans une version ne rend pas
« conforme » un passage ultérieur, un autre environnement ou le critère
EXI. Une ancienne source déplacée reste visible avec un avertissement,
mais ne certifie aucune couverture actuelle.

Ce premier lien est une *vue historique*, **pas** un contrôleur de preuve,
de source actuelle ou de conformité. Le statut métier de l'exigence et
les décisions de campagne sont conservés indépendamment. La version
courante du test reste à vérifier explicitement avec le service REC de
validité documentaire. Aucune mutation ni accès réseau.

Les régressions synthétiques couvrent un résultat ancien Passed suivi
d'un nouveau passage sans résultat, deux versions/environnements,
un projet voisin partageant un préfixe de chemin, un GUID reproduit,
une source déplacée et un critère sans test.

## Consultation des passages REC depuis la matrice (03/11/2026)

La commande « Matrice de traçabilité des exigences » joint maintenant les
**résultats historiques de campagnes** aux tests identifiés de chaque
critère. L'ensemble est calculé depuis les sources du **projet courant**
sur un worker, avec actualisation de l'index partagé et lecture des
campagnes REC. Les champs de recherche, filtres et sélection ne déclenchent
aucune nouvelle analyse disque.

La matrice affiche le nombre de sélections figées et de verdicts déclarés.
Après sélection d'un critère, puis d'un test lié, le détail montre les
passages historiques (20 derniers à l'écran) : campagne, numéro, date,
environnement, version, SHA de définition figée, acteur, résultat,
justification et note de preuve éventuelle. L'absence de résultat s'affiche
« NON EXÉCUTÉ » ; un résultat Passed est étiqueté « DÉCLARÉ ».

Une source historique déplacée est signalée explicitement, et la
comparaison au contenu actuel est présentée comme **non effectuée**.
Une campagne REC illisible n'interrompt pas la matrice des liens : un
diagnostic indique que l'historique est indisponible/partiel au lieu
d'inventer des résultats. Cette vue ne modifie ni les exigences, ni
les campagnes, ni les cahiers. La vérification de preuve et toute
transition du statut EXI exigent une action humaine distincte.

Qualification visuelle à réaliser sur Windows (thème sombre, navigation,
plusieurs campagnes, changement rapide de critères, statut sans résultat).

Les compteurs globaux de passages et de verdicts REC dédupliquent la clé
(campagne, passage, test). Un même test relié à plusieurs critères CA
reste visible dans chacun d'eux, mais ne représente qu'une seule exécution
humaine dans le total du projet.

## Requalification conseillée après modification CA (04/11/2026)

Avant de publier un **titre ou corps de critère CA existant** depuis une
réponse IA externe explicitement acceptée, Nodalis calcule à la demande
une analyse d'impact consultative, sans second référentiel. Le moteur
**ProjectCriterionChangeImpact.Build** joint la matrice EXI→CA→TEST
aux passages REC historiques du même projet.

Le calcul vérifie les GUID de projet, EXI et CA et l'empreinte SHA
du document EXI relu par l'utilisateur. Une identité répétée, un index
obsolète, une source modifiée depuis la revue ou un historique qui ne
correspond pas produit « impact indéterminé » — jamais « aucun test ».

La confirmation affiche le nombre de tests distincts liés, les
verdicts humains historiques, les passages REC et les sources
historiques de test déplacées. Un test lié mais jamais exécuté reste
à examiner ; un précédent Passed ne prouve pas la conformité à la
nouvelle formulation CA.

Après une publication réussie, l'index partagé est invalidé pour
que la prochaine matrice relise la source. Aucun état d'EXI,
relation de test, passage, verdict ou preuve n'est automatiquement
modifié.

**Limite :** la recommandation concerne les modifications CA explicites
par l'import IA. Les changements manuels de Markdown n'écrivent pas
encore de marqueur durable de requalification, et aucune preuve n'est
vérifiée par cette analyse.


## Suivi durable des modifications CA, y compris hors import IA (04/11/2026)

Le registre EXI et la matrice de traçabilité disposent désormais
d'un **suivi opt-in** indépendant du mécanisme d'import IA. Dans
« Matrice de traçabilité des exigences », la commande
**« Activer suivi CA… »** permet d'enregistrer, après confirmation
explicite, les empreintes des critères identifiés.

Un instantané portable, `baseline-000001.json`, est placé dans le
dossier du projet `.nodalis-ca-baselines/`. Il enregistre les GUID
CA et EXI, leurs références, le chemin d'origine, l'empreinte
SHA-256 de la fiche, du titre et du corps du critère. **Le corps
Markdown complet n'est pas dupliqué** dans cet historique.

À chaque ouverture de la matrice, Nodalis recalcule l'état du
projet et le compare au dernier instantané enregistré. Cette
comparaison détecte donc une modification manuelle dans un
éditeur Markdown, même si l'import IA n'est jamais utilisé.
Les états sont indépendants des résultats de recette :
`NotTracked`, `Unchanged`, `Changed`, `Added`,
`Missing`, `Unresolved`.

La matrice présente le nombre de changements, un filtre dédié
« CA modifiés depuis référence » et les diagnostics de sources
manquantes ou illisibles. Une exigence EXI devenue illisible
n'est jamais interprétée comme un CA supprimé. Une modification
étrangère au titre ou au corps du CA ne provoque pas de
requalification artificielle.

**« Acter nouvelle référence… »** nécessite un motif humain et
conserve l'ancienne observation : `baseline-000002.json`,
puis `baseline-000003.json`, etc. Les fichiers sont append-only,
les séquences sont contrôlées et chaque observation référence
l'empreinte du fichier précédent. Le service vérifie à nouveau
les SHA des EXI et refuse un catalogue obsolète, une collision
de GUID, un projet étranger/archivé, un journal invalide ou
un enregistrement simultané. Aucun fichier EXI n'est écrit
par cette fonctionnalité.

Attention : **acter une référence CA ne vaut pas requalification
d'un test ni approbation métier**. Cela fige uniquement un
nouvel état d'observation. Si des critères ont changé, les
tests concernés doivent être examinés et, si nécessaire,
réexécutés via un nouveau passage REC. Les anciens verdicts,
campagnes, liens, preuves et statuts ne sont jamais modifiés.

Qualification Windows : activer sur un projet synthétique ;
modifier directement un CA depuis un éditeur Markdown ;
rouvrir la matrice et vérifier « Changed » ; modifier uniquement
une autre rubrique EXI et vérifier « Unchanged » ; ajouter
un CA et vérifier « Added » ; supprimer un GUID CA et
vérifier « Missing » ; acter après revue et vérifier que
l'ancien instantané reste lisible ; essayer depuis un
projet archivé/lecture seule ; vérifier recherche/filtres
et affichage en thème sombre. En absence de référence
initiale, Nodalis doit afficher « non suivi », jamais « stable ».


## Sélection ciblée des tests concernés par le suivi CA — 05/11/2026

La matrice de traçabilité conserve les références CA humaines append-only. Le gestionnaire REC peut maintenant exploiter leur différence avec le Markdown actuel pour **proposer** les tests à reprendre. Les changements de titre ou corps CA sont identifiés par GUID et SHA, puis reliés aux TEST-/TST- disponibles via l'index de liens existant, sans nouveau registre de correspondances.

Deux CA peuvent déclencher **le même test** : une seule proposition apparaît, mais tous les CA déclencheurs restent visibles dans ses motifs. Les CA disparus ou illisibles ne peuvent pas être rattachés avec certitude à leurs anciens tests à partir des seuls liens actuels ; Nodalis les signale pour examen manuel et ne présume pas d'un test à rejouer.

Le bouton **« Proposer tests des CA modifiés… »** dans les campagnes REC présente cette sélection, puis ouvre le sélecteur de passage existant après accord explicite. La sélection reste éditable. Aucun résultat historique n'est modifié ; seules une sélection et un éventuel nouveau passage sont enregistrés après confirmation humaine et vérification des sources.

Une première **« Activer suivi CA… »** dans la matrice demeure nécessaire. Les modifications antérieures à ce premier instantané ne peuvent pas être reconstituées automatiquement. Les résultats d'une campagne REC et le statut de vérification EXI restent totalement indépendants de cette suggestion.
