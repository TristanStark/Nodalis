# Pilotage 2.0 — accès aux parcours, prompts et outils projet

## Objectif

Toutes les commandes de pilotage de Nodalis 2.0 sont regroupées dans
**Pilotage**, en haut à droite de la fenêtre principale. Elles réutilisent
les vues et services déjà en place. Le menu ne crée aucun document,
parcours, prompt ou incident simplement parce qu'il est ouvert.

## Où trouver chaque fonction

| Menu Pilotage | Accès et portée |
| --- | --- |
| **Parcours projet · 2.0 → Ouvrir le parcours du projet** | Onglet dédié avec étapes, opérations, jalons, gates et sources |
| **Parcours projet · 2.0 → Rechercher une étape ou un prompt** | Recherche dans toutes les étapes et opérations du projet, y compris les noms personnalisés et références FRT-100 ; actions Ouvrir, Préparer/copier et Associer selon le contexte |
| **Parcours projet · 2.0 → Toutes les étapes du projet** | Liste dynamique des étapes *réelles du projet courant*, y compris étapes renommées, réordonnées ou personnalisées. Développer une étape puis une opération pour l'afficher, préparer son prompt ou rejoindre son association de prompt |
| **Parcours projet · 2.0 → Modèles de parcours** | Éditeur des modèles de parcours du workspace ; aucune modification des parcours existants sans confirmation |
| **Prompts et réponses IA → Préparer / copier les prompts du parcours** | Étape → opération → préparation locale du brouillon, choix éventuel des liaisons de variables, revue, copie séparée des prompts système et utilisateur |
| **Prompts et réponses IA → Associer un prompt à une opération** | Étape → opération ; sélection de l'opération réelle puis **ouverture automatique du sélecteur humain de prompts**, sans clic supplémentaire ni association automatique |
| **Prompts et réponses IA → Analyser / importer une réponse IA externe** | Prévisualisation locale, comparaison, décisions humaines ; aucune IA locale obligatoire |
| **Prompts et réponses IA → Prompt de mise à jour depuis une réunion** | Workflow antérieur de synthèse de réunion / préparation de prompt, distinct des prompts FRT-100 des opérations |
| **Exigences, critères et tests · 2.0** | Nouvelle exigence EXI, traçabilité EXI ↔ CA ↔ TEST, campagnes et passages REC ; **accès direct** à la fenêtre des anomalies REC, incidents et revalidation humaine ; GUID des blocs |
| **Risques, décisions et Go/No-Go · 2.0** | Registre RIS, registre des gates / réserves / Go-No-Go et décisions du projet |
| **Préparation / migration · 2.0** | Prévisualisation, migration explicite et restauration de la structure Complexe |
| **Maintenance** | Historique, nouvel incident, nouvelle intervention |
| **Pilotage général** | Tâches, jalons, calendrier, Kanban, health check et couverture / cohérence |

Les menus **chargent uniquement les en-têtes d'étapes** au
premier survol. Les opérations d'une étape sont matérialisées en
contrôles WPF **lorsqu'on développe cette étape**, puis réutilisées
jusqu'à la fermeture du menu parent, pour éviter de
reconstruire inutilement des milliers d'éléments graphiques. Aucune
opération ne disparaît : la projection métier conserve toutes les
opérations des étapes personnalisées. Le test de régression couvre un
parcours fictif de **80 étapes et 4 800 opérations**.

Les étapes détaillées de **préparation, de validation, de mise en
production et de clôture** sont celles du **parcours propre au projet**,
pas une liste fixe construite dans le menu. Les modèles Simple, Moyen et
Complexe, et toutes les personnalisations, restent donc synchronisés
avec la liste dynamique.

## Préparation IA : schéma de réponse et opérations en lot

Dans l'onglet **Parcours**, l'action **Lier des fichiers…** accepte
désormais une sélection multiple (Ctrl/Shift dans le sélecteur Windows).
Les fichiers doivent déjà appartenir au même workspace ; Nodalis valide
toute la sélection, ignore les doublons et effectue **une seule sauvegarde
confirmée** du Parcours, sans toucher au contenu des fichiers.

Le bouton **Préparer tous les prompts** rassemble en une action les
opérations dont un prompt est associé, dans l'ordre des étapes du projet.
La préparation conserve le **prompt système FRT-100 séparé** du message
utilisateur. Le brouillon global est modifiable, copiable et exportable,
sans exécuter de modèle IA. Les sources sont limitées par opération
(64 fichiers référencés, 12 000 caractères par fichier et 60 000 caractères
d'extraits répartis entre les fichiers textuels), et le lot entier est
plafonné à 500 000 caractères.
Les avertissements indiquent les limites, les sources manquantes et les
variables qui devront être renseignées manuellement. Si un lot est
tronqué, les opérations restantes doivent être préparées séparément.

**Tous les brouillons individuels et groupés contiennent le contrat
de sortie JSON `nodalis.import.v1`**, avec un exemple complet et les
valeurs autorisées. Dans le LLM externe, coller le prompt utilisateur
préparé, ajouter le système FRT-100 séparément si nécessaire, puis
coller le JSON renvoyé dans **Analyser une réponse IA…**. Les propositions
ne sont jamais validées ni publiées sans revue humaine.

## Règles d'utilisation

1. Sélectionner d'abord un projet ou un document **de ce projet** dans
   l'explorateur. Lorsque l'**onglet Parcours est affiché**, ce dernier
   est le contexte prioritaire (il l'emporte sur une sélection
   précédente dans l'explorateur). Les commandes **« Ouvrir le parcours »**
   et **« Ouvrir / initialiser »** utilisent également ce projet
   explicite ; elles ne reprennent plus une ancienne sélection Worktree. Les commandes générales restent
   disponibles sans sélection de projet ; les vues liées à une campagne
   ou un parcours demandent un projet non ambigu.
2. Ouvrir **Pilotage → Parcours projet → Toutes les étapes du projet**.
   Les noms et opérations sont relus **à chaque ouverture** depuis
   `.project-journey.json`, hors du thread UI. L'affichage numérote
   les étapes **1, 2, 3…** selon leur ordre courant, y compris si
   l'ordre technique du parcours a été réordonné ou renuméroté de zéro. Si le parcours n'est pas
   initialisé, une entrée **« Ouvrir / initialiser le parcours… »** est
   proposée : son activation **ne lance pas** l'initialisation sans
   le choix et la confirmation de l'utilisateur. Pour les modèles
   applicables, les étapes et opérations proposées peuvent être
   consultées dans le menu **avant création**, clairement marquées
   comme aperçu et sans commande d'édition.
3. Pour retrouver rapidement une étape ou un prompt, ouvrir
   **Pilotage → Parcours projet → Rechercher une étape ou un prompt…**.
   La recherche locale ignore la casse et les accents et porte sur
   les noms d'étapes, opérations et références FRT-100. Elle indexe
   toutes les opérations mais n'affiche que **250 résultats à la fois** ;
   affiner les termes révèle les autres résultats. La recherche est
   **temporisée et calculée hors du thread WPF** ; une réponse
   ancienne est ignorée si le texte a changé.
4. Pour copier un prompt, ouvrir **Pilotage → Prompts… → Préparer / copier
   les prompts du parcours**, choisir l'étape puis l'opération. Un
   brouillon est calculé à partir des sources approuvées et des variables.
   Il est **relisible et éditable avant copie**. La copie du prompt
   utilisateur est indépendante de la copie du système FRT-100, et aucun
   modèle d'IA n'est exécuté automatiquement. Si le parcours ou le
   contexte projet a été modifié pendant la saisie des variables,
   Nodalis **refuse d'afficher un brouillon devenu obsolète** et
   demande de relancer la préparation.
5. Pour changer une association de prompt, choisir l'opération via
   **Associer un prompt à une opération**. Nodalis sélectionne cette
   opération dans l'onglet Parcours et **ouvre directement** son
   sélecteur de prompts, même si l'onglet était encore en chargement.
   Le changement reste soumis à la confirmation explicite déjà en
   place et au contrôle de révision. Annuler ne modifie aucun fichier.
6. Les options liées à l'écriture sont indisponibles pour un
   workspace en lecture seule ou un projet archivé. Le menu préserve
   les possibilités de consultation des parcours et des brouillons.
7. Si le projet, l'étape ou l'opération change entre l'ouverture du
   sous-menu et le clic, Nodalis refuse de réutiliser un GUID ou un
   chemin périmé. Le menu doit être rouvert.

**Périmètre** : les entrées ouvrent les fonctionnalités
**actuellement implémentées** ; une étape de parcours sans automate
spécialisé reste accessible dans l'onglet Parcours. Le menu ne simule
pas une mise en production ni une clôture automatiquement effectuée.

## Recette Windows manuelle

- Ouvrir le menu avec un projet **Complexe** initialisé et vérifier
  que **chacune des 9 étapes** et chaque opération du parcours apparaît,
  sans élément oublié, même si une opération n'a aucun prompt associé.
  Les tests smoke doivent contrôler la projection métier correspondante.
- Renommer, déplacer et ajouter une étape/opération depuis le Parcours,
  rouvrir le menu : le nouveau libellé, l'ordre **1..N** et les liens
  apparaissent immédiatement. Vérifier les parcours dont l'éditeur a
  renuméroté les étapes à partir de **zéro** ; un ancien élément devenu
  invalide est refusé.
- Naviguer entre **deux projets différents** pendant un chargement
  lent : aucune action sur un autre projet ne doit être possible.
  Si l'onglet Parcours du projet A est visible mais que l'explorateur
  conserve une sélection du projet B, **Pilotage doit afficher A**.
  Vérifier aussi **« Ouvrir le parcours »** et, sur un projet non
  initialisé, **« Ouvrir / initialiser »** : aucun de ces accès
  ne doit ouvrir B. En quittant ce parcours, le contexte redevient
  la sélection de l'explorateur.
- Ouvrir la liste des étapes sans parcours initialisé : aucune
  écriture, option d'ouverture / initialisation visible.
- Préparer/copier un prompt **avec** et **sans** variables, puis
  avec un prompt système distinct, en vérifiant les avertissements de
  sources et l'absence d'envoi automatique. Depuis **Associer un
  prompt à une opération**, le sélecteur doit apparaître directement
  sur la bonne opération, sans enregistrer si l'utilisateur annule.
- Dans l'onglet Parcours, vérifier que la sélection pointe vers
  **la bonne étape et opération**, y compris quand l'onglet était
  déjà ouvert. Modifier le parcours depuis un autre processus pendant
  que l'onglet reste ouvert : le raccourci Pilotage doit synchroniser
  la vue avant d'ouvrir le sélecteur. Si une deuxième modification
  intervient pendant cette synchronisation, la sélection périmée
  doit être refusée sans changement.
- Ouvrir **Associer un prompt**, puis modifier la sélection de l'étape
  ou de l'opération pendant le chargement du catalogue ou pendant
  l'affichage du sélecteur : aucune référence de prompt ne doit être
  associée à une autre opération. Annuler doit rester sans effet.
- Préparer un prompt depuis l'onglet Parcours avec un dialogue de
  variables ouvert pendant une édition externe du parcours :
  le brouillon ne doit pas être présenté comme à jour et aucune
  copie ne doit avoir lieu automatiquement.
- Ouvrir REC, anomalies, traçabilité EXI, RIS et Go/No-Go depuis
  Pilotage ; confirmer la même logique métier et les mêmes restrictions
  que via la palette ou l'onglet Parcours.
- Vérifier un projet archivé et un workspace en lecture seule :
  consultation possible mais aucune édition ni association implicite.
- Rechercher `preparation` pour retrouver `Préparation` sans
  accent et `frt100-01` pour retrouver une opération mappée.
  Une recherche courante (> 250 correspondances) doit demander un
  affinage, sans perdre aucune des opérations indexées. Tester les
  frappes rapides, l'annulation, le double-clic hors résultat et
  l'affichage en lecture seule.
- Naviguer au clavier dans les sous-menus imbriqués, contrôler les
  intitulés, le thème sombre et les longues listes d'opérations.
  Sur un projet volumineux, l'ouverture de Pilotage ne doit pas
  créer tous les contrôles WPF des opérations ; seule l'étape
  survolée les matérialise.
  Renommer une opération en `Copier_PROMPT` : le caractère souligné
  doit rester visible dans le menu sans créer de raccourci clavier.

Aucune capture d'écran ni nom réel de workspace ne doit être
ajouté aux commits ou aux jeux de test.
