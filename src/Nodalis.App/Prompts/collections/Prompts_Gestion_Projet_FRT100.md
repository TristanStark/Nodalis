# Bibliothèque de prompts — Gestion de projet technique
**Compatible avec FRT-100 — Français Rationalisé Technique**

Ces prompts sont conçus pour être utilisés comme **prompts utilisateur** avec FRT-100 chargé dans le prompt système.

## Convention

Les variables entre chevrons doivent être remplacées ou supprimées :

- `<DOCUMENT>` : spécification, compte rendu, ticket, mail, code, etc.
- `<CONTEXTE>` : contexte métier ou technique.
- `<OBJECTIF>` : résultat attendu.
- `<CONTRAINTES>` : contraintes connues.
- `<DECISION>` : décision à examiner.
- `<CHANGEMENT>` : modification envisagée.

Quand une information manque, le modèle doit la signaler explicitement. Il ne doit pas l'inventer.

---

# 1. Extraire les exigences d'une spécification

Analyse le document suivant et extrais toutes les exigences explicites et implicites.

Pour chaque exigence, fournis :
- un identifiant unique ;
- le type : fonctionnelle, non fonctionnelle, métier, technique, sécurité, exploitation ou contrainte ;
- l'exigence reformulée de manière testable ;
- la source ou le passage d'origine ;
- les dépendances ;
- les hypothèses associées ;
- le niveau d'ambiguïté ;
- les critères d'acceptation possibles.

Sépare clairement :
- ce qui est explicitement demandé ;
- ce qui est seulement déduit ;
- ce qui reste inconnu.

Ne complète pas les informations manquantes.

<DOCUMENT>

---

# 2. Transformer une spécification en données structurées

Transforme le document suivant en structure exploitable pour le suivi de projet.

Extrais au minimum :
- objectifs ;
- périmètre inclus ;
- périmètre exclu ;
- acteurs ;
- systèmes concernés ;
- exigences ;
- règles métier ;
- données manipulées ;
- interfaces ;
- dépendances ;
- contraintes ;
- jalons ;
- critères d'acceptation ;
- risques ;
- questions ouvertes.

Retourne d'abord un tableau synthétique, puis une liste des éléments impossibles à classer avec certitude.

<DOCUMENT>

---

# 3. Analyser ce qui manque dans une spécification

Agis comme un reviewer technique exigeant.

Analyse cette spécification et recherche ce qui manque pour permettre :
1. le développement ;
2. les tests ;
3. la mise en production ;
4. l'exploitation ;
5. la maintenance.

Classe les manques par criticité :
- BLOQUANT ;
- IMPORTANT ;
- UTILE ;
- OPTIONNEL.

Pour chaque manque :
- explique pourquoi il est important ;
- indique le risque associé ;
- propose la question exacte à poser ;
- indique quel rôle devrait fournir la réponse.

Ne suppose pas que les éléments non mentionnés existent.

<DOCUMENT>

---

# 4. Détecter les ambiguïtés et contradictions

Analyse le document suivant pour identifier :
- contradictions ;
- exigences incompatibles ;
- termes ambigus ;
- références imprécises ;
- valeurs non définies ;
- comportements non spécifiés ;
- exceptions manquantes ;
- dépendances circulaires ;
- formulations impossibles à tester.

Pour chaque problème :
- cite ou résume les passages concernés ;
- explique le problème ;
- évalue son impact ;
- propose une reformulation non ambiguë.

<DOCUMENT>

---

# 5. Générer les questions de clarification

À partir du document suivant, génère uniquement les questions qui doivent être posées avant de commencer l'implémentation.

Priorise les questions qui peuvent modifier :
- l'architecture ;
- le périmètre ;
- le planning ;
- le coût ;
- les tests ;
- la sécurité ;
- la production.

Classe les questions dans l'ordre où elles doivent être résolues.

Pour chaque question, précise :
- pourquoi elle est nécessaire ;
- la décision qui dépend de la réponse ;
- le rôle susceptible de répondre.

<DOCUMENT>

---

# 6. Identifier les hypothèses cachées

Recherche toutes les hypothèses implicites du document suivant.

Pour chaque hypothèse :
- formule-la explicitement ;
- indique ce qui permet de la déduire ;
- estime le risque si elle est fausse ;
- indique comment la vérifier ;
- indique si elle doit devenir une exigence explicite.

Ne transforme pas une hypothèse en fait.

<DOCUMENT>

---

# 7. Construire des critères d'acceptation

À partir des exigences suivantes, construis des critères d'acceptation vérifiables.

Pour chaque exigence :
- propose des critères nominaux ;
- propose des cas limites ;
- propose des cas d'erreur ;
- propose des conditions de refus ;
- indique les données nécessaires au test.

Les critères doivent être observables et mesurables.

Évite les termes subjectifs comme :
- rapide ;
- intuitif ;
- robuste ;
- simple ;
- correct ;
- performant.

Si un critère ne peut pas être rendu mesurable, signale l'information manquante.

<EXIGENCES>

---

# 8. Générer les tests unitaires à prévoir

Analyse cette spécification ou ce code et construis le plan de tests unitaires.

Pour chaque unité logique, identifie :
- comportement nominal ;
- valeurs limites ;
- entrées invalides ;
- valeurs nulles ou absentes ;
- erreurs attendues ;
- effets de bord ;
- dépendances à mocker ;
- invariants ;
- régressions probables.

Présente les tests sous la forme :

ID | Élément testé | Précondition | Entrée | Résultat attendu | Priorité

Ne génère du code de test que si je le demande explicitement.

<SOURCE>

---

# 9. Générer les tests d'intégration

Construis un plan de tests d'intégration pour le système décrit ci-dessous.

Identifie :
- interfaces entre composants ;
- contrats d'API ;
- échanges de fichiers ;
- accès aux bases de données ;
- appels externes ;
- authentification ;
- gestion des erreurs ;
- reprise après échec ;
- concurrence ;
- ordre des traitements ;
- cohérence transactionnelle.

Pour chaque test, précise :
- composants impliqués ;
- scénario ;
- données d'entrée ;
- résultat attendu ;
- méthode de vérification ;
- dépendances externes ;
- criticité.

<SOURCE>

---

# 10. Construire une matrice de traçabilité

Construis une matrice de traçabilité à partir des éléments fournis.

Relie autant que possible :
- besoin métier ;
- exigence ;
- tâche ou composant ;
- test ;
- critère d'acceptation ;
- risque ;
- documentation associée.

Signale :
- les exigences sans test ;
- les tests sans exigence ;
- les besoins sans implémentation identifiable ;
- les composants sans justification fonctionnelle.

<SOURCE>

---

# 11. Challenger une solution technique

Analyse la solution proposée comme un architecte technique chargé de la challenger.

Examine :
- cohérence avec le besoin ;
- complexité inutile ;
- dépendances ;
- couplage ;
- maintenabilité ;
- performances ;
- montée en charge ;
- observabilité ;
- sécurité ;
- reprise sur erreur ;
- migration ;
- exploitation ;
- coût opérationnel ;
- dette technique ;
- réversibilité.

Pour chaque critique :
- explique le problème ;
- indique sa probabilité ;
- indique son impact ;
- propose une alternative ;
- précise le coût ou le compromis de l'alternative.

Ne cherche pas à défendre la solution proposée.

<SOLUTION>

---

# 12. Comparer plusieurs solutions

Compare les solutions suivantes pour répondre à cet objectif :

<OBJECTIF>

Solutions :
<SOLUTIONS>

Évalue chaque solution selon :
- adéquation fonctionnelle ;
- complexité ;
- coût d'implémentation ;
- coût d'exploitation ;
- délai ;
- risques ;
- maintenabilité ;
- compétences nécessaires ;
- dépendances ;
- sécurité ;
- performances ;
- évolutivité ;
- réversibilité.

Distingue :
- faits connus ;
- hypothèses ;
- estimations ;
- inconnues.

Termine par :
- la solution recommandée ;
- les raisons ;
- les conditions qui pourraient changer cette recommandation.

---

# 13. Préparer une décision

Prépare une fiche de décision à partir du contexte suivant :

<CONTEXTE>

Décision à prendre :
<DECISION>

Structure :
1. problème ;
2. objectif ;
3. contraintes ;
4. options réalistes ;
5. avantages ;
6. inconvénients ;
7. risques ;
8. coûts ;
9. dépendances ;
10. conséquences à court terme ;
11. conséquences à long terme ;
12. éléments manquants ;
13. recommandation ;
14. niveau de confiance.

Ne masque pas les incertitudes.

---

# 14. Faire un pré-mortem

Suppose que le projet ou la décision suivante a échoué dans 12 mois.

<PROJET_OU_DECISION>

Identifie les causes plausibles de cet échec.

Pour chaque cause :
- décris le mécanisme d'échec ;
- estime la probabilité ;
- estime l'impact ;
- indique les signes précurseurs ;
- propose une mesure préventive ;
- propose un plan de secours.

Cherche en priorité les risques que l'équipe pourrait sous-estimer.

---

# 15. Construire un registre des risques

Analyse les informations suivantes et construis un registre des risques.

<SOURCE>

Pour chaque risque :
- ID ;
- description ;
- cause ;
- conséquence ;
- probabilité ;
- impact ;
- criticité ;
- indicateur précurseur ;
- prévention ;
- mitigation ;
- plan de secours ;
- propriétaire suggéré ;
- date ou événement de réévaluation.

Sépare les risques avérés des hypothèses de risque.

---

# 16. Analyser l'impact d'un changement

Analyse l'impact du changement suivant :

<CHANGEMENT>

Contexte :
<CONTEXTE>

Recherche les impacts sur :
- exigences ;
- architecture ;
- interfaces ;
- données ;
- sécurité ;
- performances ;
- code ;
- tests ;
- documentation ;
- migration ;
- déploiement ;
- exploitation ;
- utilisateurs ;
- planning ;
- coûts ;
- dépendances externes.

Classe chaque impact :
- certain ;
- probable ;
- possible ;
- inconnu.

Signale les régressions potentielles et les vérifications à effectuer avant validation.

---

# 17. Vérifier la préparation au développement

Détermine si ce sujet est suffisamment défini pour être développé.

<SOURCE>

Vérifie :
- objectif clair ;
- périmètre ;
- règles métier ;
- entrées ;
- sorties ;
- erreurs ;
- interfaces ;
- cas limites ;
- contraintes ;
- dépendances ;
- sécurité ;
- critères d'acceptation ;
- stratégie de test ;
- données de test.

Retourne :
- PRÊT ;
- PRÊT AVEC RÉSERVES ;
- NON PRÊT.

Justifie le verdict par des éléments concrets.

---

# 18. Vérifier la préparation à la mise en production

Évalue si cette évolution est prête pour la production.

<SOURCE>

Vérifie :
- tests ;
- couverture des exigences ;
- migration ;
- sauvegarde ;
- rollback ;
- monitoring ;
- logs ;
- alertes ;
- capacité ;
- performances ;
- sécurité ;
- droits ;
- documentation d'exploitation ;
- support ;
- procédures d'incident ;
- dépendances ;
- communication utilisateurs.

Classe les écarts :
- BLOQUANT PROD ;
- RISQUE ACCEPTABLE ;
- AMÉLIORATION.

Ne déclare pas la mise en production prête si un prérequis critique n'est pas démontré.

---

# 19. Transformer un compte rendu en actions

Analyse ce compte rendu.

<COMPTE_RENDU>

Extrais :
- décisions prises ;
- actions ;
- responsable de chaque action ;
- échéance ;
- dépendances ;
- questions ouvertes ;
- décisions en attente ;
- risques mentionnés ;
- points nécessitant une validation.

Ne déduis pas un responsable ou une date absente.

Ajoute une section :
**Éléments mentionnés mais non conclus**.

---

# 20. Comparer deux versions d'une spécification

Compare les deux versions suivantes.

VERSION A :
<VERSION_A>

VERSION B :
<VERSION_B>

Identifie :
- ajouts ;
- suppressions ;
- modifications ;
- changements de comportement ;
- changements de contraintes ;
- changements de vocabulaire susceptibles de modifier le sens ;
- impacts sur les tests ;
- impacts sur les interfaces ;
- risques de régression.

Ignore les changements purement stylistiques sauf s'ils créent une ambiguïté.

---

# 21. Faire une revue documentaire complète

Effectue une revue complète du document suivant.

<DOCUMENT>

Évalue séparément :
- exactitude ;
- complétude ;
- cohérence ;
- ambiguïtés ;
- structure ;
- terminologie ;
- testabilité ;
- traçabilité ;
- maintenabilité ;
- exploitabilité.

Pour chaque problème :
- indique la section concernée ;
- classe la gravité ;
- explique le problème ;
- propose une correction.

Termine par les cinq corrections les plus importantes.

---

# 22. Trouver les cas limites

Recherche systématiquement les cas limites du comportement suivant :

<SOURCE>

Examine au minimum :
- zéro ;
- minimum ;
- maximum ;
- valeurs négatives ;
- valeurs vides ;
- doublons ;
- données incohérentes ;
- données obsolètes ;
- ordre inattendu ;
- concurrence ;
- interruption ;
- timeout ;
- resoumission ;
- traitement partiel ;
- dépendance indisponible ;
- volume extrême ;
- droits insuffisants.

Ignore les catégories non pertinentes.

Pour chaque cas pertinent, indique le comportement attendu ou signale qu'il n'est pas spécifié.

---

# 23. Identifier les scénarios oubliés

À partir des scénarios déjà documentés, recherche les scénarios importants qui ne sont pas couverts.

<SCENARIOS_EXISTANTS>

Contexte :
<CONTEXTE>

Recherche notamment :
- échec partiel ;
- reprise ;
- annulation ;
- répétition ;
- concurrence ;
- changement d'état pendant l'opération ;
- erreur utilisateur ;
- erreur système ;
- perte de connexion ;
- dépendance externe indisponible ;
- données anciennes ;
- données contradictoires ;
- permissions insuffisantes ;
- changement de version.

Ne propose que des scénarios plausibles dans le contexte.

---

# 24. Vérifier les dépendances

Analyse les dépendances du sujet suivant.

<SOURCE>

Identifie :
- dépendances internes ;
- dépendances externes ;
- dépendances humaines ;
- dépendances organisationnelles ;
- dépendances de données ;
- dépendances d'environnement ;
- dépendances temporelles.

Pour chaque dépendance :
- indique ce qui en dépend ;
- indique ce qui se passe si elle n'est pas disponible ;
- précise si un découplage est possible ;
- propose une mitigation.

Signale les dépendances non documentées mais fortement suggérées par le contexte.

---

# 25. Construire une checklist projet spécifique

Construis une checklist de revue pour le sujet suivant :

<SUJET>

Contexte :
<CONTEXTE>

La checklist doit être adaptée au sujet. Elle ne doit pas être générique.

Couvre uniquement les dimensions pertinentes parmi :
- besoin ;
- périmètre ;
- métier ;
- architecture ;
- données ;
- API ;
- sécurité ;
- conformité ;
- performance ;
- tests ;
- déploiement ;
- exploitation ;
- documentation ;
- support ;
- migration ;
- rollback ;
- observabilité.

Chaque entrée doit pouvoir recevoir :
- OUI ;
- NON ;
- NON APPLICABLE ;
- À CONFIRMER.

---

# 26. Faire l'avocat du diable

Challenge la proposition suivante sans chercher à la valider.

<PROPOSITION>

Recherche :
- ce qui pourrait être faux ;
- ce qui est supposé sans preuve ;
- ce qui est sous-estimé ;
- les effets secondaires ;
- les coûts cachés ;
- les dépendances ignorées ;
- les scénarios où la solution échoue ;
- les alternatives plus simples ;
- les décisions difficiles à inverser.

Pour chaque critique, indique si elle est :
- forte ;
- plausible ;
- spéculative.

Ne crée pas de problème artificiel uniquement pour remplir la liste.

---

# 27. Revue "avons-nous pensé à tout ?"

Effectue une revue transversale du sujet suivant :

<SOURCE>

Cherche ce que l'équipe peut avoir oublié dans les domaines suivants :
- utilisateurs ;
- règles métier ;
- données ;
- erreurs ;
- sécurité ;
- droits ;
- performances ;
- volumétrie ;
- concurrence ;
- dépendances ;
- exploitation ;
- observabilité ;
- reprise ;
- sauvegarde ;
- migration ;
- compatibilité ;
- tests ;
- documentation ;
- formation ;
- support ;
- conformité ;
- planning ;
- responsabilités.

Pour chaque point manquant :
- explique pourquoi il peut devenir un problème ;
- indique quand il doit être traité ;
- propose la prochaine action.

Ignore les domaines clairement non pertinents.

---

# 28. Synthèse pour décision rapide

Résume le sujet suivant pour une personne qui doit prendre une décision sans lire toute la documentation.

<SOURCE>

Limite la réponse à :
- objectif ;
- situation actuelle ;
- décision nécessaire ;
- options ;
- recommandation ;
- trois risques principaux ;
- éléments encore inconnus ;
- prochaine action.

Ne supprime pas une incertitude importante pour rendre la synthèse plus simple.

---

# 29. Créer une documentation manquante

À partir des sources suivantes, identifie les sections de documentation qui peuvent être rédigées de façon fiable.

<SOURCES>

Pour chaque section possible :
- propose un titre ;
- indique les sources utilisées ;
- indique le niveau de confiance ;
- rédige le contenu uniquement si les sources sont suffisantes.

Crée ensuite une section :
**Documentation impossible à compléter sans information supplémentaire**

Pour chaque élément manquant, indique précisément l'information requise.

---

# 30. Audit final avant clôture

Effectue un audit final du sujet avant clôture.

<SOURCE>

Vérifie :
- exigences traitées ;
- critères d'acceptation satisfaits ;
- anomalies restantes ;
- risques acceptés ;
- dette technique créée ;
- documentation mise à jour ;
- tests présents ;
- déploiement documenté ;
- exploitation documentée ;
- décisions archivées ;
- actions restantes ;
- propriétaires identifiés.

Classe les résultats :
- TERMINÉ ;
- RESTE À FAIRE ;
- ACCEPTÉ COMME DETTE ;
- NON DÉMONTRÉ.

Ne considère pas un élément comme terminé sans preuve dans les sources fournies.

---

# Prompt combiné — Revue complète d'un sujet

Utilise ce prompt lorsqu'une seule revue approfondie est préférable à plusieurs analyses séparées.

Analyse le sujet suivant comme un reviewer projet, fonctionnel et technique exigeant.

<SOURCE>

Effectue successivement :

1. résume l'objectif réel ;
2. extrais les exigences ;
3. identifie les hypothèses ;
4. détecte les ambiguïtés et contradictions ;
5. identifie ce qui manque ;
6. recherche les dépendances ;
7. recherche les risques ;
8. recherche les cas limites ;
9. propose les critères d'acceptation ;
10. propose les tests nécessaires ;
11. évalue la préparation au développement ;
12. génère les questions encore ouvertes.

Pour chaque conclusion, distingue :
- FACTUEL ;
- DÉDUIT ;
- HYPOTHÈSE ;
- INCONNU.

Termine par :
- les 5 points les plus critiques ;
- les décisions nécessaires ;
- les prochaines actions recommandées.

Ne complète jamais une information absente pour rendre le dossier artificiellement complet.